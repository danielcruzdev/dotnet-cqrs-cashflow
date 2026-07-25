using Lancamentos.Application.Abstracoes;
using Lancamentos.Domain;
using Lancamentos.Domain.Abstracoes;
using Lancamentos.Domain.Eventos;

namespace Lancamentos.Application.CriarLancamento;

/// <summary>
/// Orquestra o registro de um lançamento: resolve idempotência, monta a entidade
/// e grava lançamento e evento na mesma transação.
/// </summary>
/// <remarks>
/// O handler <b>orquestra</b>, não decide regra de negócio: quem valida valor,
/// precisão, data de competência e estorno é a entidade <see cref="Lancamento"/>.
/// Aqui só existe a sequência de passos do caso de uso.
/// </remarks>
public sealed class CriarLancamentoCommandHandler(
    ILancamentoRepository repositorio,
    IOutboxWriter outbox,
    IUnitOfWork unitOfWork,
    IRelogio relogio)
    : ICommandHandler<CriarLancamentoCommand, ResultadoCriarLancamento>
{
    public async Task<ResultadoCriarLancamento> ExecutarAsync(
        CriarLancamentoCommand comando,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(comando);

        var hashPayload = comando.CalcularHashPayload();

        // 1. Caminho rápido: a chave já foi usada? Cobre o retry comum sem
        //    tocar em transação nem gerar contenção.
        var existente = await repositorio.ObterPorChaveIdempotenciaAsync(
            comando.ComercianteId, comando.ChaveIdempotencia, cancellationToken);

        if (existente is not null)
        {
            return Classificar(existente, hashPayload);
        }

        if (!TipoLancamentoExtensions.TentarConverter(comando.Tipo, out var tipo))
        {
            throw new DominioException(
                "lancamento.tipo_invalido",
                $"Tipo de lançamento inválido: '{comando.Tipo}'. Esperado DEBITO ou CREDITO.");
        }

        var valor = Dinheiro.Criar(comando.Valor, Moeda.Criar(comando.Moeda));

        var lancamento = Lancamento.Criar(
            comercianteId: comando.ComercianteId,
            tipo: tipo,
            valor: valor,
            dataCompetencia: comando.DataCompetencia,
            descricao: comando.Descricao,
            chaveIdempotencia: comando.ChaveIdempotencia,
            hashPayload: hashPayload,
            relogio: relogio);

        var evento = LancamentoRealizado.De(lancamento, comando.CorrelationId);

        try
        {
            // 2. A invariante central do sistema: lançamento e evento na MESMA
            //    transação local. Ou os dois são gravados, ou nenhum é. É o que
            //    garante que todo lançamento aceito tem exatamente um evento
            //    pendente de publicação — sem transação distribuída.
            await unitOfWork.ExecutarAsync(async ct =>
            {
                await repositorio.AdicionarAsync(lancamento, ct);
                await outbox.EscreverAsync(evento, ct);
            }, cancellationToken);
        }
        catch (ChaveIdempotenciaEmUsoException)
        {
            // 3. Corrida perdida: entre o passo 1 e aqui, outra requisição com a
            //    mesma chave inseriu primeiro. A constraint única do banco é a
            //    autoridade final — o vencedor é quem já está lá. Reler e
            //    classificar transforma a corrida em resposta correta, em vez
            //    de devolver 500 para um cliente que só fez retry.
            var vencedor = await repositorio.ObterPorChaveIdempotenciaAsync(
                comando.ComercianteId, comando.ChaveIdempotencia, cancellationToken);

            if (vencedor is null)
            {
                // A constraint disparou mas o registro não está visível. Não é um
                // caso previsto do protocolo de idempotência, então sobe.
                throw;
            }

            return Classificar(vencedor, hashPayload);
        }

        return ResultadoCriarLancamento.Criado(lancamento);
    }

    /// <summary>
    /// Decide entre retry legítimo e reuso indevido de chave comparando o hash.
    /// </summary>
    private static ResultadoCriarLancamento Classificar(Lancamento existente, string hashPayload) =>
        string.Equals(existente.HashPayload, hashPayload, StringComparison.OrdinalIgnoreCase)
            ? ResultadoCriarLancamento.JaRegistrado(existente)
            : ResultadoCriarLancamento.ConflitoDeChave(existente);
}
