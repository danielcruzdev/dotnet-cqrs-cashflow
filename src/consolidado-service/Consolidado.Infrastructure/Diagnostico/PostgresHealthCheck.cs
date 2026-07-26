using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Consolidado.Infrastructure.Diagnostico;

/// <summary>
/// Única dependência de readiness do Consolidado. Redis fica de fora de
/// propósito: o cache é otimização de latência, não requisito para servir.
/// </summary>
public sealed class PostgresHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var conexao = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var comando = conexao.CreateCommand();
            comando.CommandText = "SELECT 1";
            await comando.ExecuteScalarAsync(cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Banco do consolidado indisponível", ex);
        }
    }
}
