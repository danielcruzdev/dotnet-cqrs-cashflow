using System.Globalization;
using Lancamentos.Api;
using Lancamentos.Api.Endpoints;
using Lancamentos.Infrastructure;

// A mesma requisição precisa ser interpretada igual em qualquer região.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DominioExceptionHandler>();

// Cobre formato; regra de negócio é do domínio.
builder.Services.AddValidation();

// Composition root: único ponto que conhece implementações concretas.
builder.Services.AdicionarInfraestruturaDeLancamentos(builder.Configuration);

// Falha ao subir, não na primeira requisição.
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
