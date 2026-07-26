using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Consolidado.Api;
using Consolidado.Api.Endpoints;
using Consolidado.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Scalar.AspNetCore;

// A mesma requisição precisa ser interpretada igual em qualquer região.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ConsultaExceptionHandler>();
builder.Services.AdicionarAutenticacaoJwt(builder.Configuration);
builder.Services.AdicionarInfraestruturaDeConsolidado(builder.Configuration);

// O documento OpenAPI é derivado dos próprios endpoints, não de um arquivo
// escrito à mão — não existe especificação para divergir do código.
builder.Services.AddOpenApi(opcoes => opcoes.AddDocumentTransformer<EsquemaBearerTransformer>());

// Deny by default: sem origem configurada, nenhum navegador de terceiro passa.
var origens = builder.Configuration.GetSection("Cors:Origens").Get<string[]>() ?? [];
builder.Services.AddCors(cors => cors.AddDefaultPolicy(politica => politica
    .WithOrigins(origens)
    .WithMethods("GET")
    .WithHeaders("Authorization", "Content-Type")));

// Teto por comerciante, bem acima dos 50 rps do SLO: barra abuso sem gastar
// o orçamento de erro em carga normal.
builder.Services.AddRateLimiter(limitador =>
{
    limitador.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limitador.OnRejected = (contexto, _) =>
    {
        contexto.HttpContext.Response.Headers.RetryAfter = "1";
        return ValueTask.CompletedTask;
    };
    limitador.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(contexto =>
        RateLimitPartition.GetFixedWindowLimiter(
            contexto.User.FindFirstValue(Autorizacao.ClaimComerciante) ?? "anonimo",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 200,
                Window = TimeSpan.FromSeconds(1),
            }));
});

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

app.Use(async (contexto, proximo) =>
{
    contexto.Response.Headers.XContentTypeOptions = "nosniff";
    contexto.Response.Headers.XFrameOptions = "DENY";
    contexto.Response.Headers["Referrer-Policy"] = "no-referrer";
    await proximo();
});

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Depois da autenticação: a partição precisa enxergar a claim do comerciante.
app.UseRateLimiter();

app.MapGet("/", () => Results.Ok(new { servico = "consolidado", versao = "0.1.0" }));

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
    .DisableRateLimiting();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = verificacao => verificacao.Tags.Contains("ready"),
}).DisableRateLimiting();

// Fora do limitador pela mesma razão dos health checks: sem token, a
// requisição cai na partição `anonimo` e um 429 aqui viraria "a documentação
// não abre" — perda causada pela própria proteção.
app.MapOpenApi().DisableRateLimiting();
app.MapScalarApiReference(opcoes => opcoes
    .WithTitle("Cashflow — Consolidado")
    .AddPreferredSecuritySchemes("Bearer"))
    .DisableRateLimiting();

app.MapearConsolidado();

app.Run();

public partial class Program;
