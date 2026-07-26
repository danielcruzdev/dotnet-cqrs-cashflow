using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Lancamentos.Api;

public static class Autorizacao
{
    public const string ClaimComerciante = "comerciante_id";

    // Sem esta checagem qualquer portador de token válido opera na conta alheia (IDOR).
    public static bool EhDono(this ClaimsPrincipal usuario, Guid comercianteId)
    {
        ArgumentNullException.ThrowIfNull(usuario);

        return Guid.TryParse(usuario.FindFirstValue(ClaimComerciante), out var id)
            && id == comercianteId;
    }

    public static ProblemHttpResult AcessoNegado() => TypedResults.Problem(
        title: "Acesso negado",
        detail: "O token apresentado não pertence ao comerciante informado.",
        statusCode: StatusCodes.Status403Forbidden,
        extensions: new Dictionary<string, object?> { ["codigo"] = "acesso.comerciante_divergente" });
}
