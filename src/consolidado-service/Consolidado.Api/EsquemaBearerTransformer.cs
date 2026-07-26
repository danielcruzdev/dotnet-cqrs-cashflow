using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Consolidado.Api;

// Declara o esquema bearer nos componentes do documento OpenAPI. Sem isso a
// referência da API não tem onde receber o token, e o testador precisaria
// montar o header Authorization a cada requisição.
//
// O esquema fica só nos componentes, sem virar requisito global de segurança:
// marcar todas as operações como autenticadas seria mentira em /api/token,
// /health/* e na própria página de documentação, que são anônimos.
internal sealed class EsquemaBearerTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument documento,
        OpenApiDocumentTransformerContext contexto,
        CancellationToken cancellationToken)
    {
        var esquema = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Token emitido por POST /api/token no serviço de Lançamentos.",
        };

        documento.Components ??= new OpenApiComponents();
        documento.AddComponent("Bearer", esquema);

        return Task.CompletedTask;
    }
}
