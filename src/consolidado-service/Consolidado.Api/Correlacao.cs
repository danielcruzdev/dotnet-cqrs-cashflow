namespace Consolidado.Api;

/// <summary>
/// Mesmo header do serviço de Lançamentos: um id de correlação atravessa a
/// escrita, a fila e a consulta, e amarra os logs dos dois serviços.
/// </summary>
public static class Correlacao
{
    public const string Header = "X-Correlation-Id";

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
}
