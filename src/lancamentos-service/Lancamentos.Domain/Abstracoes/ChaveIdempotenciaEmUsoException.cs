namespace Lancamentos.Domain.Abstracoes;

/// <summary>
/// A chave de idempotência já está em uso por outro lançamento do mesmo comerciante.
/// </summary>
public sealed class ChaveIdempotenciaEmUsoException(Guid comercianteId, string chave, Exception? innerException = null)
    : Exception($"A chave de idempotência '{chave}' já está em uso pelo comerciante {comercianteId}.", innerException)
{
    public Guid ComercianteId { get; } = comercianteId;

    public string Chave { get; } = chave;
}
