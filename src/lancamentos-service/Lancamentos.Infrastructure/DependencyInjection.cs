using Dapper;
using Lancamentos.Application.Abstracoes;
using Lancamentos.Application.ConsultarLancamentos;
using Lancamentos.Application.CriarLancamento;
using Lancamentos.Application.EstornarLancamento;
using Lancamentos.Domain;
using Lancamentos.Domain.Abstracoes;
using Lancamentos.Infrastructure.Diagnostico;
using Lancamentos.Infrastructure.Mensageria;
using Lancamentos.Infrastructure.Persistencia;
using Lancamentos.Infrastructure.Tempo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Lancamentos.Infrastructure;

/// <summary>
/// Composição da infraestrutura do serviço de Lançamentos.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AdicionarInfraestruturaDeLancamentos(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        ConfigurarDapper();

        var connectionString = configuration.GetConnectionString("LancamentosDb")
            ?? throw new InvalidOperationException(
                "A connection string 'LancamentosDb' não está configurada.");

        // Singleton: é o dono do pool de conexões.
        services.AddSingleton(_ =>
        {
            var builder = new NpgsqlDataSourceBuilder(connectionString);
            return builder.Build();
        });

        services.AddScoped<SessaoDeBanco>();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IRelogio>(sp => new RelogioDoComerciante(
            sp.GetRequiredService<TimeProvider>(),
            configuration["Lancamentos:FusoHorario"]));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ILancamentoRepository, LancamentoRepository>();
        services.AddScoped<OutboxRepository>();
        services.AddScoped<IOutboxWriter>(sp => sp.GetRequiredService<OutboxRepository>());

        // Registro explícito, sem varredura por reflexão.
        services.AddScoped<
            ICommandHandler<CriarLancamentoCommand, ResultadoCriarLancamento>,
            CriarLancamentoCommandHandler>();

        services.AddScoped<
            ICommandHandler<EstornarLancamentoCommand, ResultadoEstorno>,
            EstornarLancamentoCommandHandler>();

        services.AddScoped<
            IQueryHandler<ObterLancamentoPorIdQuery, Lancamento?>,
            ObterLancamentoPorIdQueryHandler>();

        services.AddScoped<
            IQueryHandler<ListarLancamentosQuery, PaginaDeLancamentos>,
            ListarLancamentosQueryHandler>();

        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.Secao));
        services.AddSingleton<ConexaoRabbitMq>();
        services.AddHostedService<OutboxPublisherBackgroundService>();

        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"]);

        return services;
    }

    private static void ConfigurarDapper()
    {
        // snake_case -> PascalCase. Sem isso o valor chega default, sem erro.
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
    }
}
