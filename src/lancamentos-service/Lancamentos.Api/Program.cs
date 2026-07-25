using System.Globalization;
using Lancamentos.Api;
using Lancamentos.Api.Endpoints;
using Lancamentos.Infrastructure;

// Formatação e parsing não podem depender da região do servidor: a mesma
// requisição precisa ser interpretada igual em Windows pt-BR e em contêiner
// Linux. Não conflita com InvariantGlobalization=false, que segue ligado
// porque os dados de ICU são necessários para resolver o fuso do comerciante.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DominioExceptionHandler>();

// Validação nativa de Minimal APIs do .NET 10, sobre DataAnnotations.
// Cobre formato; regra de negócio continua sendo do domínio.
builder.Services.AddValidation();

// Composition root: único ponto que conhece implementações concretas.
builder.Services.AdicionarInfraestruturaDeLancamentos(builder.Configuration);

// Falha ao subir, não na primeira requisição: valida todo o grafo de
// dependências e detecta captive dependency (singleton segurando scoped).
builder.Host.UseDefaultServiceProvider(opcoes =>
{
    opcoes.ValidateOnBuild = true;
    opcoes.ValidateScopes = true;
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapGet("/", () => Results.Ok(new { servico = "lancamentos", versao = "0.1.0" }));

app.MapearLancamentos();

app.Run();

// Necessário para que WebApplicationFactory<Program> enxergue a classe
// gerada pelos top-level statements nos testes de integração.
public partial class Program;
