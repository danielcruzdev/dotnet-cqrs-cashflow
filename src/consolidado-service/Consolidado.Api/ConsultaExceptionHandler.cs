using Consolidado.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Consolidado.Api;

/// <summary>
/// Traduz falhas de consulta e de degradação em ProblemDetails.
/// </summary>
public sealed class ConsultaExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var (status, titulo, codigo) = exception switch
        {
            ConsultaInvalidaException e => (StatusCodes.Status400BadRequest, "Consulta inválida", e.Codigo),

            // Circuito aberto: o banco está degradado e falhar rápido preserva o
            // orçamento de erro em vez de empilhar requisições.
            BrokenCircuitException => (StatusCodes.Status503ServiceUnavailable,
                "Serviço temporariamente indisponível", "consolidado.circuito_aberto"),

            TimeoutRejectedException => (StatusCodes.Status504GatewayTimeout,
                "Tempo de consulta excedido", "consolidado.timeout"),

            _ => (0, string.Empty, string.Empty),
        };

        if (status == 0)
        {
            return false;
        }

        var problema = new ProblemDetails
        {
            Type = $"https://cashflow.local/erros/{codigo}",
            Title = titulo,
            Status = status,
            Detail = exception.Message,
            Instance = httpContext.Request.Path,
        };

        problema.Extensions["codigo"] = codigo;

        if (status is StatusCodes.Status503ServiceUnavailable or StatusCodes.Status504GatewayTimeout)
        {
            httpContext.Response.Headers.RetryAfter = "5";
        }

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problema, cancellationToken);

        return true;
    }
}
