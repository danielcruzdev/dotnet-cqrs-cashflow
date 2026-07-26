using Lancamentos.Application.Abstracoes;
using Lancamentos.Domain;
using Lancamentos.Domain.Abstracoes;
using Lancamentos.Domain.Eventos;

namespace Lancamentos.Application.EstornarLancamento;

public sealed record EstornarLancamentoCommand : ICommand<ResultadoEstorno>
{
    public required Guid ComercianteId { get; init; }
    public required Guid LancamentoId { get; init; }
    public required string ChaveIdempotencia { get; init; }
    public required Guid CorrelationId { get; init; }
}

public enum StatusEstorno
{
    Criado = 1,
    OriginalNaoEncontrado = 2,
    JaEstornado = 3,
    JaRegistrado = 4,
    ConflitoDeChave = 5
}

public sealed record ResultadoEstorno(StatusEstorno Status, Lancamento? Estorno);

public sealed class EstornarLancamentoCommandHandler(
    ILancamentoRepository repositorio,
    IOutboxWriter outbox,
    IUnitOfWork unitOfWork,
    IRelogio relogio)
    : ICommandHandler<EstornarLancamentoCommand, ResultadoEstorno>
{
    public async Task<ResultadoEstorno> ExecutarAsync(
        EstornarLancamentoCommand comando,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(comando);

        var hashPayload = HashDoEstorno(comando.LancamentoId);

        var jaProcessado = await repositorio.ObterPorChaveIdempotenciaAsync(
            comando.ComercianteId, comando.ChaveIdempotencia, cancellationToken);

        if (jaProcessado is not null)
        {
            return string.Equals(jaProcessado.HashPayload, hashPayload, StringComparison.OrdinalIgnoreCase)
                ? new ResultadoEstorno(StatusEstorno.JaRegistrado, jaProcessado)
                : new ResultadoEstorno(StatusEstorno.ConflitoDeChave, jaProcessado);
        }

        var original = await repositorio.ObterPorIdAsync(
            comando.ComercianteId, comando.LancamentoId, cancellationToken);

        if (original is null)
        {
            return new ResultadoEstorno(StatusEstorno.OriginalNaoEncontrado, null);
        }

        if (await repositorio.PossuiEstornoAsync(original.Id, cancellationToken))
        {
            return new ResultadoEstorno(StatusEstorno.JaEstornado, null);
        }

        // Estornar um estorno é recusado pela própria entidade.
        var estorno = original.Estornar(comando.ChaveIdempotencia, hashPayload, relogio);

        var evento = LancamentoRealizado.De(estorno, comando.CorrelationId);

        await unitOfWork.ExecutarAsync(async ct =>
        {
            await repositorio.AdicionarAsync(estorno, ct);
            await outbox.EscreverAsync(evento, ct);
        }, cancellationToken);

        return new ResultadoEstorno(StatusEstorno.Criado, estorno);
    }

    /// <summary>O "payload" de um estorno é o id do lançamento revertido.</summary>
    private static string HashDoEstorno(Guid lancamentoId)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"estorno|{lancamentoId:N}"));

        return Convert.ToHexStringLower(bytes);
    }
}
