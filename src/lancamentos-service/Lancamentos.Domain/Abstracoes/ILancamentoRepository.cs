namespace Lancamentos.Domain.Abstracoes;

/// <summary>Acesso ao repositório de lançamentos.</summary>
/// <remarks>
/// Não existe <c>Atualizar</c> nem <c>Remover</c>: lançamento é imutável, e a
/// ausência desses métodos torna a regra impossível de violar por descuido, em
/// vez de depender de alguém lembrar dela na revisão de código.
/// </remarks>
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
    /// <remarks>
    /// A chave é única <b>por comerciante</b>, não globalmente: dois comerciantes
    /// podem legitimamente usar "pedido-123". Por isso o <paramref name="comercianteId"/>
    /// é obrigatório aqui.
    /// </remarks>
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
