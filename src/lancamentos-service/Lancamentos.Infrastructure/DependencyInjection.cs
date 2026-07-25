using Dapper;
using Lancamentos.Application.Abstracoes;
using Lancamentos.Application.CriarLancamento;
using Lancamentos.Domain;
using Lancamentos.Domain.Abstracoes;
using Lancamentos.Infrastructure.Persistencia;
using Lancamentos.Infrastructure.Tempo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Lancamentos.Infrastructure;

/// <summary>
/// Composição da infraestrutura do serviço de Lançamentos.
/// </summary>
/// <remarks>
/// Este é o único ponto do sistema em que interfaces do domínio são amarradas a
/// implementações concretas. A <c>Api</c> chama este método e não conhece
/// nenhum tipo de infraestrutura além dele.
/// </remarks>
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

        // NpgsqlDataSource é singleton por design: ele é dono do pool de
        // conexões. Criar um por requisição anularia o pool inteiro.
        services.AddSingleton(_ =>
        {
            var builder = new NpgsqlDataSourceBuilder(connectionString);
            return builder.Build();
        });

        // Uma sessão por requisição — é o que faz o lançamento e a linha da
        // outbox caírem na mesma transação.
        services.AddScoped<SessaoDeBanco>();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IRelogio>(sp => new RelogioDoComerciante(
            sp.GetRequiredService<TimeProvider>(),
            configuration["Lancamentos:FusoHorario"]));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ILancamentoRepository, LancamentoRepository>();
        services.AddScoped<OutboxRepository>();
        services.AddScoped<IOutboxWriter>(sp => sp.GetRequiredService<OutboxRepository>());

        // Handlers registrados explicitamente, sem varredura por reflexão. Com
        // poucos casos de uso, a lista explícita é mais rápida de ler, falha em
        // tempo de compilação se um tipo sumir, e não esconde registro mágico.
        services.AddScoped<
            ICommandHandler<CriarLancamentoCommand, ResultadoCriarLancamento>,
            CriarLancamentoCommandHandler>();

        return services;
    }

    private static void ConfigurarDapper()
    {
        // Nomes de coluna em snake_case mapeiam para propriedades em PascalCase.
        // Sem isso, data_competencia não encontraria DataCompetencia e o valor
        // chegaria default sem nenhum erro — falha silenciosa, a pior categoria.
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
    }
}
