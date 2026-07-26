namespace Lancamentos.Api;

/// <summary>
/// Lê (ou gera) o identificador de correlação, ecoa na resposta e o coloca no
/// escopo de log. Daqui ele segue no evento até o consumer do Consolidado.
/// </summary>
public static class Correlacao
{
    public const string Header = "X-Correlation-Id";

    private const string ChaveItem = "correlationId";

    public static IApplicationBuilder UsarCorrelacao(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var logger = app.ApplicationServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Correlacao");

        return app.Use(async (contexto, proximo) =>
        {
            var id = Guid.TryParse(contexto.Request.Headers[Header], out var informado)
                ? informado
                : Guid.CreateVersion7();

            contexto.Items[ChaveItem] = id;

            // OnStarting: o middleware de exceção limpa os headers já escritos,
            // e é na resposta de erro que o id mais importa.
            contexto.Response.OnStarting(() =>
            {
                contexto.Response.Headers[Header] = id.ToString();
                return Task.CompletedTask;
            });

            using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
            {
                await proximo();
            }
        });
    }

    public static Guid Obter(HttpContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        return (Guid)contexto.Items[ChaveItem]!;
    }
}
