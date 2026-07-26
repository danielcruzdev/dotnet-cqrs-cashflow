using Consolidado.Application.Abstracoes;
using Consolidado.Application.ConsultarSaldo;
using Consolidado.Domain;
using Consolidado.Infrastructure.Mensageria;
using Consolidado.Infrastructure.Persistencia;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

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

        services.AddStackExchangeRedisCache(opcoes =>
        {
            opcoes.Configuration = configuration.GetConnectionString("Redis") ?? "localhost:6379";
            opcoes.InstanceName = "cashflow:";
        });

        services.AddSingleton(ConstruirResiliencia());
        services.AddScoped<ISaldoDiarioRepository, SaldoDiarioRepository>();

        services.AddScoped<
            IQueryHandler<ObterSaldoDiarioQuery, SaldoDiario>,
            ObterSaldoDiarioQueryHandler>();

        services.AddScoped<
            IQueryHandler<ObterSaldoPeriodoQuery, SaldoPeriodo>,
            ObterSaldoPeriodoQueryHandler>();

        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.Secao));
        services.AddHostedService<LancamentoRealizadoConsumer>();

        return services;
    }

    /// <summary>
    /// Timeout curto e circuito que abre com metade das chamadas falhando.
    /// Falhar rápido cabe no orçamento de erro; empilhar requisições não.
    /// </summary>
    private static ResiliencePipeline ConstruirResiliencia() =>
        new ResiliencePipelineBuilder()
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(2),
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                MinimumThroughput = 10,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(15),
            })
            .Build();
}
