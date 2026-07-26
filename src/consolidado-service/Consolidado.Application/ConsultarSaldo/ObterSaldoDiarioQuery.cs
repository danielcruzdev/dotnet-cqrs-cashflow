using Consolidado.Application.Abstracoes;
using Consolidado.Domain;

namespace Consolidado.Application.ConsultarSaldo;

public sealed record ObterSaldoDiarioQuery(Guid ComercianteId, DateOnly Data, string Moeda)
    : IQuery<SaldoDiario>;

public sealed class ObterSaldoDiarioQueryHandler(ISaldoDiarioRepository repositorio)
    : IQueryHandler<ObterSaldoDiarioQuery, SaldoDiario>
{
    public async Task<SaldoDiario> ExecutarAsync(
        ObterSaldoDiarioQuery consulta,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consulta);

        var saldo = await repositorio.ObterAsync(
            consulta.ComercianteId, consulta.Data, consulta.Moeda, cancellationToken);

        // Dia sem lançamentos devolve zeros, não 404.
        return saldo ?? SaldoDiario.Vazio(consulta.ComercianteId, consulta.Data, consulta.Moeda);
    }
}
