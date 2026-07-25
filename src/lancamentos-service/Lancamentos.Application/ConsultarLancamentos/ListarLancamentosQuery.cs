using Lancamentos.Application.Abstracoes;
using Lancamentos.Domain;
using Lancamentos.Domain.Abstracoes;

namespace Lancamentos.Application.ConsultarLancamentos;

public sealed record ListarLancamentosQuery : IQuery<PaginaDeLancamentos>
{
    public const int TamanhoPaginaPadrao = 50;
    public const int TamanhoPaginaMaximo = 200;

    public required Guid ComercianteId { get; init; }
    public required DateOnly DataInicio { get; init; }
    public required DateOnly DataFim { get; init; }
    public int Pagina { get; init; } = 1;
    public int TamanhoPagina { get; init; } = TamanhoPaginaPadrao;
}

public sealed record PaginaDeLancamentos(
    IReadOnlyList<Lancamento> Itens,
    int Pagina,
    int TamanhoPagina,
    int Total)
{
    public int TotalDePaginas => Total == 0 ? 0 : (int)Math.Ceiling(Total / (double)TamanhoPagina);
}

public sealed class ListarLancamentosQueryHandler(ILancamentoRepository repositorio)
    : IQueryHandler<ListarLancamentosQuery, PaginaDeLancamentos>
{
    public async Task<PaginaDeLancamentos> ExecutarAsync(
        ListarLancamentosQuery consulta,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consulta);

        if (consulta.DataInicio > consulta.DataFim)
        {
            throw new DominioException(
                "consulta.periodo_invalido",
                "A data inicial não pode ser posterior à data final.");
        }

        var pagina = Math.Max(1, consulta.Pagina);
        var tamanho = Math.Clamp(
            consulta.TamanhoPagina,
            1,
            ListarLancamentosQuery.TamanhoPaginaMaximo);

        var total = await repositorio.ContarPorPeriodoAsync(
            consulta.ComercianteId, consulta.DataInicio, consulta.DataFim, cancellationToken);

        // Evita ir ao banco quando o offset já passou do fim do conjunto.
        if (total == 0)
        {
            return new PaginaDeLancamentos([], pagina, tamanho, 0);
        }

        var itens = await repositorio.ListarPorPeriodoAsync(
            consulta.ComercianteId, consulta.DataInicio, consulta.DataFim,
            pagina, tamanho, cancellationToken);

        return new PaginaDeLancamentos(itens, pagina, tamanho, total);
    }
}
