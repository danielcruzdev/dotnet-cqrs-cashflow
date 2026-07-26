using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Lancamentos.Api.Endpoints;

// Emissor local para o desafio rodar sem dependência externa. Em produção o
// token viria de um IdP (Keycloak, Entra ID) e os serviços só validariam.
public static class TokenEndpoints
{
    public static IEndpointRouteBuilder MapearToken(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/token", Emitir)
            .AllowAnonymous()
            .WithName("EmitirToken")
            .WithSummary("Emite um token de desenvolvimento para um comerciante");

        return app;
    }

    private static Ok<TokenResponse> Emitir(
        EmitirTokenRequest request,
        [FromServices] JwtOptions opcoes)
    {
        var expiraEm = DateTime.UtcNow.AddMinutes(opcoes.ValidadeMinutos);

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = opcoes.Issuer,
            Audience = opcoes.Audience,
            Expires = expiraEm,
            Subject = new ClaimsIdentity(
                [new Claim(Autorizacao.ClaimComerciante, request.ComercianteId.ToString())]),
            SigningCredentials = new SigningCredentials(
                opcoes.ChaveDeAssinatura(), SecurityAlgorithms.HmacSha256),
        });

        return TypedResults.Ok(new TokenResponse(token, expiraEm));
    }
}

public sealed record EmitirTokenRequest(Guid ComercianteId);

public sealed record TokenResponse(string Token, DateTime ExpiraEm);
