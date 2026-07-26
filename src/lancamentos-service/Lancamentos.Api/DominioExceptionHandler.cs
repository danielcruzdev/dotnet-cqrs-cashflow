using Lancamentos.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Lancamentos.Api;

/// <summary>
/// Traduz <see cref="DominioException"/> em <c>ProblemDetails</c> (RFC 7807).
/// </summary>
public sealed partial class DominioExceptionHandler(ILogger<DominioExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (exception is not DominioException dominio)
        {
            return false;
        }

        var status = StatusPara(dominio.Codigo);

        // Violação de regra é esperada, não incidente: não polui o alerta de 5xx.
        LogRegraViolada(logger, dominio.Codigo, dominio.Message);

        var problema = new ProblemDetails
        {
            Type = $"https://cashflow.local/erros/{dominio.Codigo}",
            Title = "Regra de negócio violada",
            Status = status,
            Detail = dominio.Message,
            Instance = httpContext.Request.Path,
        };

        problema.Extensions["codigo"] = dominio.Codigo;
        problema.Extensions["correlationId"] = httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problema, cancellationToken);

        return true;
    }

    private static int StatusPara(string codigo) => codigo switch
    {
        // Requisição bem formada, regra insatisfazível.
        "lancamento.estorno_de_estorno" => StatusCodes.Status422UnprocessableEntity,

        // O estado do recurso conflita com a operação pedida.
        "lancamento.ja_estornado" => StatusCodes.Status409Conflict,

        _ => StatusCodes.Status400BadRequest,
    };

    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Regra de negócio violada: {Codigo} — {Mensagem}")]
    private static partial void LogRegraViolada(ILogger logger, string codigo, string mensagem);
}
