using Consolidado.Application.Abstracoes;
using Consolidado.Domain;

namespace Consolidado.Application.ConsultarSaldo;

public sealed record ObterSaldoPeriodoQuery(Guid ComercianteId, DateOnly De, DateOnly Ate, string Moeda)
    : IQuery<SaldoPeriodo>;

public sealed record SaldoPeriodo(
    Guid ComercianteId,
    string Moeda,
    DateOnly De,
    DateOnly Ate,
    IReadOnlyList<SaldoDiario> Dias,
    decimal SaldoDoPeriodo);

public sealed class ObterSaldoPeriodoQueryHandler(ISaldoDiarioRepository repositorio)
    : IQueryHandler<ObterSaldoPeriodoQuery, SaldoPeriodo>
{
    public const int MaximoDeDias = 90;

    public async Task<SaldoPeriodo> ExecutarAsync(
        ObterSaldoPeriodoQuery consulta,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consulta);

        var moeda = MoedaDeConsulta.Normalizar(consulta.Moeda);

        if (consulta.De > consulta.Ate)
        {
            throw new ConsultaInvalidaException(
                "consulta.periodo_invalido", "A data inicial não pode ser posterior à data final.");
        }

        // Limite explícito evita que uma consulta acidental de anos vire incidente.
        if (consulta.Ate.DayNumber - consulta.De.DayNumber + 1 > MaximoDeDias)
        {
            throw new ConsultaInvalidaException(
                "consulta.periodo_muito_longo", $"O período não pode exceder {MaximoDeDias} dias.");
        }

        var dias = await repositorio.ListarPeriodoAsync(
            consulta.ComercianteId, consulta.De, consulta.Ate, moeda, cancellationToken);

        return new SaldoPeriodo(
            consulta.ComercianteId,
            moeda,
            consulta.De,
            consulta.Ate,
            dias,
            dias.Sum(d => d.Saldo));
    }
}
