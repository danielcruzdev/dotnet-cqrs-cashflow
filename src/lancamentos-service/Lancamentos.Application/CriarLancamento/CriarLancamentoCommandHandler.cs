using Lancamentos.Application.Abstracoes;
using Lancamentos.Domain;
using Lancamentos.Domain.Abstracoes;
using Lancamentos.Domain.Eventos;

namespace Lancamentos.Application.CriarLancamento;

/// <summary>
/// Orquestra o registro de um lançamento: resolve idempotência, monta a entidade
/// e grava lançamento e evento na mesma transação.
/// </summary>
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

        // Caminho rápido do retry: evita abrir transação.
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
            // Lançamento e evento na mesma transação (Outbox).
            await unitOfWork.ExecutarAsync(async ct =>
            {
                await repositorio.AdicionarAsync(lancamento, ct);
                await outbox.EscreverAsync(evento, ct);
            }, cancellationToken);
        }
        catch (ChaveIdempotenciaEmUsoException)
        {
            // Outra requisição com a mesma chave inseriu primeiro: relê e devolve o vencedor.
            var vencedor = await repositorio.ObterPorChaveIdempotenciaAsync(
                comando.ComercianteId, comando.ChaveIdempotencia, cancellationToken);

            if (vencedor is null)
            {
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
