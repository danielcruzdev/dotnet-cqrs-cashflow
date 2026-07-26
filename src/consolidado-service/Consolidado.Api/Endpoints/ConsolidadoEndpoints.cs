using System.Security.Claims;
using Consolidado.Application.Abstracoes;
using Consolidado.Application.ConsultarSaldo;
using Consolidado.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Consolidado.Api.Endpoints;

public static class ConsolidadoEndpoints
{
    private const string MoedaPadrao = "BRL";

    public static IEndpointRouteBuilder MapearConsolidado(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/consolidado")
            .WithTags("Consolidado")
            .RequireAuthorization();

        grupo.MapGet("/{comercianteId:guid}/{data}", ObterDiaAsync)
            .WithName("ObterSaldoDiario");

        grupo.MapGet("/{comercianteId:guid}", ObterPeriodoAsync)
            .WithName("ObterSaldoPeriodo");

        return app;
    }

    private static async Task<Results<Ok<SaldoDiario>, ProblemHttpResult>> ObterDiaAsync(
        Guid comercianteId,
        DateOnly data,
        ClaimsPrincipal usuario,
        [FromServices] IQueryHandler<ObterSaldoDiarioQuery, SaldoDiario> handler,
        CancellationToken cancellationToken,
        [FromQuery] string moeda = MoedaPadrao)
    {
        if (!usuario.EhDono(comercianteId))
        {
            return Autorizacao.AcessoNegado();
        }

        var saldo = await handler.ExecutarAsync(
            new ObterSaldoDiarioQuery(comercianteId, data, moeda), cancellationToken);

        return TypedResults.Ok(saldo);
    }

    private static async Task<Results<Ok<SaldoPeriodo>, ProblemHttpResult>> ObterPeriodoAsync(
        Guid comercianteId,
        [FromQuery] DateOnly de,
        [FromQuery] DateOnly ate,
        ClaimsPrincipal usuario,
        [FromServices] IQueryHandler<ObterSaldoPeriodoQuery, SaldoPeriodo> handler,
        CancellationToken cancellationToken,
        [FromQuery] string moeda = MoedaPadrao)
    {
        if (!usuario.EhDono(comercianteId))
        {
            return Autorizacao.AcessoNegado();
        }

        var periodo = await handler.ExecutarAsync(
            new ObterSaldoPeriodoQuery(comercianteId, de, ate, moeda), cancellationToken);

        return TypedResults.Ok(periodo);
    }
}
