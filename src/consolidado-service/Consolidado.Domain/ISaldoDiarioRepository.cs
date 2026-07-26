namespace Consolidado.Domain;

public interface ISaldoDiarioRepository
{
    /// <summary>
    /// Aplica o evento à projeção. Dedupe e soma acontecem na mesma transação.
    /// </summary>
    /// <returns><c>false</c> se o evento já havia sido processado.</returns>
    Task<bool> AplicarAsync(LancamentoRealizado evento, CancellationToken cancellationToken = default);

    Task<SaldoDiario?> ObterAsync(
        Guid comercianteId,
        DateOnly data,
        string moeda,
        CancellationToken cancellationToken = default);
}
