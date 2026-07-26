using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using RabbitMQ.Client;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace Cashflow.E2E.Tests;

/// <summary>
/// Sobe a infraestrutura em containers e hospeda as duas APIs em processo.
/// Os dois bancos são separados, como em produção: é o requisito âncora.
/// </summary>
public sealed class AmbienteCashflow : IAsyncLifetime
{
    private const string Usuario = "cashflow";
    private const string Senha = "cashflow_dev";
    private const string ChaveJwt = "chave-de-teste-do-cashflow-com-tamanho-suficiente-para-hmacsha256";

    // Porta fixa: o teste de resiliência para e sobe o broker, e uma porta
    // dinâmica seria realocada na volta — o publisher perderia o endereço.
    private static readonly int PortaBroker = PortaLivre();

    private readonly PostgreSqlContainer _bancoLancamentos = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("lancamentos_db")
        .WithUsername(Usuario)
        .WithPassword(Senha)
        .Build();

    private readonly PostgreSqlContainer _bancoConsolidado = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("consolidado_db")
        .WithUsername(Usuario)
        .WithPassword(Senha)
        .Build();

    // Mesma topologia do compose: exchange, fila, binding e DLX vêm do definitions.json.
    private readonly RabbitMqContainer _broker = new RabbitMqBuilder()
        .WithImage("rabbitmq:4-management-alpine")
        .WithUsername(Usuario)
        .WithPassword(Senha)
        .WithPortBinding(PortaBroker, 5672)
        .WithResourceMapping(File.ReadAllBytes(Arquivo("definitions.json")), "/etc/rabbitmq/definitions.json")
        .WithResourceMapping(File.ReadAllBytes(Arquivo("rabbitmq.conf")), "/etc/rabbitmq/rabbitmq.conf")
        .Build();

    private readonly RedisContainer _cache = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private WebApplicationFactory<Lancamentos.Api.JwtOptions> _lancamentos = null!;
    private WebApplicationFactory<Consolidado.Api.JwtOptions> _consolidado = null!;

    public string ConexaoLancamentos => _bancoLancamentos.GetConnectionString();

    public string ConexaoConsolidado => _bancoConsolidado.GetConnectionString();

    private string BrokerHost => _broker.Hostname;

    private int BrokerPorta => _broker.GetMappedPublicPort(5672);

    /// <summary>Conexão direta com o broker, para reentregar um evento à mão.</summary>
    public ConnectionFactory FabricaDoBroker => new()
    {
        HostName = BrokerHost,
        Port = BrokerPorta,
        UserName = Usuario,
        Password = Senha,
    };

    /// <summary>
    /// Dia contábil corrente no fuso do comerciante. O cache do Consolidado dá
    /// TTL de 5s ao dia corrente e de 5min aos passados; usar o dia corrente
    /// mantém curta a janela em que uma leitura concorrente fixa valor velho.
    /// </summary>
    public static DateOnly HojeDoComerciante => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo")).DateTime);

    public async Task InitializeAsync()
    {
        await Task.WhenAll(
            _bancoLancamentos.StartAsync(),
            _bancoConsolidado.StartAsync(),
            _broker.StartAsync(),
            _cache.StartAsync());

        await ExecutarSchemaAsync(ConexaoLancamentos, "lancamentos-schema.sql");
        await ExecutarSchemaAsync(ConexaoConsolidado, "consolidado-schema.sql");

        PublicarConfiguracaoNoAmbiente();

        _lancamentos = new WebApplicationFactory<Lancamentos.Api.JwtOptions>();
        _consolidado = new WebApplicationFactory<Consolidado.Api.JwtOptions>();

        // Criar o cliente sobe o host: é o que inicia o publisher e o consumer.
        _lancamentos.CreateClient().Dispose();
        _consolidado.CreateClient().Dispose();
    }

    public async Task DisposeAsync()
    {
        await _lancamentos.DisposeAsync();
        await _consolidado.DisposeAsync();

        await Task.WhenAll(
            _bancoLancamentos.DisposeAsync().AsTask(),
            _bancoConsolidado.DisposeAsync().AsTask(),
            _broker.DisposeAsync().AsTask(),
            _cache.DisposeAsync().AsTask());
    }

    public Task<HttpClient> ClienteLancamentosAsync(Guid comercianteId) =>
        AutenticarAsync(_lancamentos, comercianteId);

    public Task<HttpClient> ClienteConsolidadoAsync(Guid comercianteId) =>
        AutenticarAsync(_consolidado, comercianteId);

    /// <summary>
    /// Derruba o serviço de Consolidado inteiro: a API e o consumer caem juntos,
    /// porque o consumer roda no mesmo processo. O banco dele continua de pé de
    /// propósito — é o que permite provar por SQL que a projeção não avançou.
    /// </summary>
    public ValueTask DerrubarConsolidadoAsync() => _consolidado.DisposeAsync();

    public void SubirConsolidado()
    {
        _consolidado = new WebApplicationFactory<Consolidado.Api.JwtOptions>();
        _consolidado.CreateClient().Dispose();
    }

    public Task DerrubarBrokerAsync() => _broker.StopAsync();

    public Task SubirBrokerAsync() => _broker.StartAsync();

    /// <summary>
    /// Repete a leitura até a condição valer ou o prazo acabar. A consistência é
    /// eventual: afirmar o saldo logo após o 201 seria testar uma corrida.
    /// </summary>
    public static async Task<T> AguardarAsync<T>(
        Func<Task<T>> ler,
        Func<T, bool> ate,
        TimeSpan? prazo = null)
    {
        var limite = DateTime.UtcNow + (prazo ?? TimeSpan.FromSeconds(30));
        T ultimo;

        do
        {
            ultimo = await ler();

            if (ate(ultimo))
            {
                return ultimo;
            }

            await Task.Delay(200);
        }
        while (DateTime.UtcNow < limite);

        return ultimo;
    }

    /// <summary>
    /// Variável de ambiente, e não <c>ConfigureAppConfiguration</c>: as duas APIs
    /// leem a connection string e a seção Jwt no corpo do <c>Program</c>, antes do
    /// <c>Build()</c>, e o que a WebApplicationFactory injeta só entra no Build.
    /// Sem isto as APIs caem no appsettings e o teste passa contra o compose local.
    /// </summary>
    private void PublicarConfiguracaoNoAmbiente()
    {
        var valores = new Dictionary<string, string>
        {
            ["ConnectionStrings__LancamentosDb"] = ConexaoLancamentos,
            ["ConnectionStrings__ConsolidadoDb"] = ConexaoConsolidado,
            ["ConnectionStrings__Redis"] = _cache.GetConnectionString(),
            ["Lancamentos__FusoHorario"] = "America/Sao_Paulo",
            ["Jwt__Issuer"] = "cashflow-auth",
            ["Jwt__Audience"] = "cashflow-api",
            ["Jwt__Chave"] = ChaveJwt,
            ["RabbitMq__Host"] = BrokerHost,
            ["RabbitMq__Porta"] = BrokerPorta.ToString(CultureInfo.InvariantCulture),
            ["RabbitMq__Usuario"] = Usuario,
            ["RabbitMq__Senha"] = Senha,
            // Varredura curta: o teste não deve esperar os 2s de produção.
            ["RabbitMq__IntervaloVarredura"] = "00:00:00.200",
            // Teto de backoff curto: com o de produção, o publisher dormiria até
            // 30s depois de uma queda do broker e o teste de resiliência esperaria junto.
            ["RabbitMq__BackoffMaximo"] = "00:00:02",
        };

        foreach (var (chave, valor) in valores)
        {
            Environment.SetEnvironmentVariable(chave, valor);
        }
    }

    private async Task<HttpClient> AutenticarAsync<T>(WebApplicationFactory<T> fabrica, Guid comercianteId)
        where T : class
    {
        var cliente = fabrica.CreateClient();

        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await TokenAsync(comercianteId));

        return cliente;
    }

    private async Task<string> TokenAsync(Guid comercianteId)
    {
        using var anonimo = _lancamentos.CreateClient();

        using var resposta = await anonimo.PostAsJsonAsync("/api/token", new { comercianteId });
        resposta.EnsureSuccessStatusCode();

        var emitido = await resposta.Content.ReadFromJsonAsync<TokenEmitido>();

        return emitido!.Token;
    }

    private static async Task ExecutarSchemaAsync(string conexao, string arquivo)
    {
        await using var conectada = new NpgsqlConnection(conexao);
        await conectada.OpenAsync();

        await using var comando = new NpgsqlCommand(await File.ReadAllTextAsync(Arquivo(arquivo)), conectada);
        await comando.ExecuteNonQueryAsync();
    }

    private static string Arquivo(string nome) => Path.Combine(AppContext.BaseDirectory, "ambiente", nome);

    private static int PortaLivre()
    {
        using var sonda = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        sonda.Bind(new IPEndPoint(IPAddress.Any, 0));

        return ((IPEndPoint)sonda.LocalEndPoint!).Port;
    }

    private sealed record TokenEmitido(string Token);
}

[CollectionDefinition(nameof(AmbienteCashflow))]
public sealed class ColecaoCashflow : ICollectionFixture<AmbienteCashflow>;
