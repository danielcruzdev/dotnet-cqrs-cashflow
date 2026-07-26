using System.Globalization;
using Lancamentos.Api;
using Lancamentos.Api.Endpoints;
using Lancamentos.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;

// A mesma requisição precisa ser interpretada igual em qualquer região.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DominioExceptionHandler>();

// Cobre formato; regra de negócio é do domínio.
builder.Services.AddValidation();

// O documento OpenAPI é derivado dos próprios endpoints, não de um arquivo
// escrito à mão — não existe especificação para divergir do código.
builder.Services.AddOpenApi(opcoes => opcoes.AddDocumentTransformer<EsquemaBearerTransformer>());

builder.Services.AdicionarAutenticacaoJwt(builder.Configuration);

// Composition root: único ponto que conhece implementações concretas.
builder.Services.AdicionarInfraestruturaDeLancamentos(builder.Configuration);

// Falha ao subir, não na primeira requisição.
builder.Host.UseDefaultServiceProvider(opcoes =>
{
    opcoes.ValidateOnBuild = true;
    opcoes.ValidateScopes = true;
});

var app = builder.Build();

app.UsarCorrelacao();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new { servico = "lancamentos", versao = "0.1.0" }));

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = verificacao => verificacao.Tags.Contains("ready"),
});

// Anônimos de propósito: quem abre a referência da API ainda não tem token,
// e é no endpoint /api/token que ele descobre como obter um.
app.MapOpenApi();
app.MapScalarApiReference(opcoes => opcoes
    .WithTitle("Cashflow — Lançamentos")
    .AddPreferredSecuritySchemes("Bearer"));

app.MapearToken();
app.MapearLancamentos();

app.Run();

// Necessário para que WebApplicationFactory<Program> enxergue a classe
// gerada pelos top-level statements nos testes de integração.
public partial class Program;
