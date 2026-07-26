using System.Diagnostics.Metrics;

namespace Consolidado.Infrastructure.Diagnostico;

/// <summary>
/// Instrumentos do serviço. Sem exportador nesta versão: os valores são lidos
/// por <c>dotnet-counters</c>. OpenTelemetry está declarado como evolução.
/// </summary>
public static class Metricas
{
    public const string Nome = "Cashflow.Consolidado";

    private static readonly Meter Medidor = new(Nome);

    /// <summary>SLI nº 6 — janela entre a escrita do lançamento e a projeção.</summary>
    public static readonly Histogram<double> LagDeConsistencia =
        Medidor.CreateHistogram<double>(
            "cashflow.consolidado.lag_consistencia",
            "ms",
            "Tempo entre o criadoEm do evento e a atualização do saldo diário");
}
