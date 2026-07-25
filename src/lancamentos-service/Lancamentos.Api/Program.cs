var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

// Endpoint de cortesia para confirmar que o serviço subiu.
// Os endpoints de negócio e os health checks são adicionados nas etapas seguintes.
app.MapGet("/", () => Results.Ok(new
{
    servico = "lancamentos",
    versao = "0.1.0"
}));

app.Run();

// Necessário para que WebApplicationFactory<Program> enxergue a classe
// gerada pelo top-level statements nos testes de integração.
public partial class Program;
