using System.Globalization;
using Consolidado.Infrastructure;

// A mesma requisição precisa ser interpretada igual em qualquer região.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AdicionarInfraestruturaDeConsolidado(builder.Configuration);

// Falha ao subir, não na primeira requisição.
builder.Host.UseDefaultServiceProvider(opcoes =>
{
    opcoes.ValidateOnBuild = true;
    opcoes.ValidateScopes = true;
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapGet("/", () => Results.Ok(new { servico = "consolidado", versao = "0.1.0" }));

app.Run();

public partial class Program;
