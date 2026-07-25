using Lancamentos.Application.Abstracoes;
using Lancamentos.Domain;
using Lancamentos.Domain.Abstracoes;

namespace Lancamentos.Application.ConsultarLancamentos;

public sealed record ObterLancamentoPorIdQuery(Guid ComercianteId, Guid Id) : IQuery<Lancamento?>;

public sealed class ObterLancamentoPorIdQueryHandler(ILancamentoRepository repositorio)
    : IQueryHandler<ObterLancamentoPorIdQuery, Lancamento?>
{
    public Task<Lancamento?> ExecutarAsync(
        ObterLancamentoPorIdQuery consulta,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consulta);

        return repositorio.ObterPorIdAsync(consulta.ComercianteId, consulta.Id, cancellationToken);
    }
}
