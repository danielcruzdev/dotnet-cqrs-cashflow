namespace Lancamentos.Infrastructure.Mensageria;

public sealed class RabbitMqOptions
{
    public const string Secao = "RabbitMq";

    public string Host { get; set; } = "localhost";
    public int Porta { get; set; } = 5672;
    public string Usuario { get; set; } = "cashflow";
    public string Senha { get; set; } = "cashflow_dev";
    public string VirtualHost { get; set; } = "/";

    public string Exchange { get; set; } = "lancamentos.events";
    public string ExchangeDlx { get; set; } = "lancamentos.events.dlx";
    public string FilaConsolidado { get; set; } = "consolidado.lancamento-realizado";
    public string FilaDlq { get; set; } = "consolidado.lancamento-realizado.dlq";

    /// <summary>Intervalo entre varreduras da outbox quando não há pendências.</summary>
    public TimeSpan IntervaloVarredura { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Quantidade de mensagens reservadas por ciclo.</summary>
    public int TamanhoLote { get; set; } = 50;

    /// <summary>Teto do backoff após falhas consecutivas de publicação.</summary>
    public TimeSpan BackoffMaximo { get; set; } = TimeSpan.FromSeconds(30);
}
