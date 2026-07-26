using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Lancamentos.Api;

public sealed class JwtOptions
{
    public const string Secao = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string Chave { get; set; } = string.Empty;
    public int ValidadeMinutos { get; set; } = 120;

    public SymmetricSecurityKey ChaveDeAssinatura() => new(Encoding.UTF8.GetBytes(Chave));
}

public static class Autenticacao
{
    public static IServiceCollection AdicionarAutenticacaoJwt(
        this IServiceCollection servicos,
        IConfiguration configuracao)
    {
        var opcoes = configuracao.GetSection(JwtOptions.Secao).Get<JwtOptions>()
            ?? throw new InvalidOperationException("Seção Jwt ausente na configuração.");

        servicos.AddSingleton(opcoes);

        servicos.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(bearer =>
            {
                // Sem isto, comerciante_id seria remapeado para um nome de claim do WS-Federation.
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = opcoes.Issuer,
                    ValidAudience = opcoes.Audience,
                    IssuerSigningKey = opcoes.ChaveDeAssinatura(),
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        servicos.AddAuthorization();

        return servicos;
    }
}
