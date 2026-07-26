using Consolidado.Domain;
using Consolidado.Infrastructure.Mensageria;
using Consolidado.Infrastructure.Persistencia;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Consolidado.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AdicionarInfraestruturaDeConsolidado(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // snake_case -> PascalCase. Sem isso o valor chega default, sem erro.
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());

        var connectionString = configuration.GetConnectionString("ConsolidadoDb")
            ?? throw new InvalidOperationException("A connection string 'ConsolidadoDb' não está configurada.");

        services.AddSingleton(_ => new NpgsqlDataSourceBuilder(connectionString).Build());
        services.AddScoped<ISaldoDiarioRepository, SaldoDiarioRepository>();

        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.Secao));
        services.AddHostedService<LancamentoRealizadoConsumer>();

        return services;
    }
}
