namespace Lancamentos.Domain.Abstracoes;

/// <summary>Acesso ao repositório de lançamentos.</summary>
public interface ILancamentoRepository
{
    Task AdicionarAsync(Lancamento lancamento, CancellationToken cancellationToken = default);

    Task<Lancamento?> ObterPorIdAsync(
        Guid comercianteId,
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Busca pela chave de idempotência dentro do escopo do comerciante.
    /// </summary>
    Task<Lancamento?> ObterPorChaveIdempotenciaAsync(
        Guid comercianteId,
        string chaveIdempotencia,
        CancellationToken cancellationToken = default);

    /// <summary>Indica se o lançamento informado já foi estornado.</summary>
    Task<bool> PossuiEstornoAsync(Guid lancamentoId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Lancamento>> ListarPorPeriodoAsync(
        Guid comercianteId,
        DateOnly inicio,
        DateOnly fim,
        int pagina,
        int tamanhoPagina,
        CancellationToken cancellationToken = default);

    Task<int> ContarPorPeriodoAsync(
        Guid comercianteId,
        DateOnly inicio,
        DateOnly fim,
        CancellationToken cancellationToken = default);
}
