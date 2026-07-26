namespace Consolidado.Infrastructure.Mensageria;

public sealed class RabbitMqOptions
{
    public const string Secao = "RabbitMq";

    public string Host { get; set; } = "localhost";
    public int Porta { get; set; } = 5672;
    public string Usuario { get; set; } = "cashflow";
    public string Senha { get; set; } = "cashflow_dev";
    public string VirtualHost { get; set; } = "/";
    public string Fila { get; set; } = "consolidado.lancamento-realizado";

    public ushort Prefetch { get; set; } = 20;
    public TimeSpan IntervaloReconexao { get; set; } = TimeSpan.FromSeconds(5);
}
