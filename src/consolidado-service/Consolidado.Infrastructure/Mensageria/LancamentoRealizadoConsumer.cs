using System.Text;
using System.Text.Json;
using Consolidado.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Consolidado.Infrastructure.Mensageria;

public sealed partial class LancamentoRealizadoConsumer(
    IServiceScopeFactory escopos,
    IOptions<RabbitMqOptions> opcoes,
    ILogger<LancamentoRealizadoConsumer> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly RabbitMqOptions _opcoes = opcoes.Value;

    private IConnection? _conexao;
    private IChannel? _canal;

    // O handler de mensagem é chamado pelo dispatcher do broker, sem token próprio.
    private CancellationToken _parada;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _parada = stoppingToken;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConectarEConsumirAsync(stoppingToken);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogConexaoFalhou(logger, ex);
                await Task.Delay(_opcoes.IntervaloReconexao, stoppingToken);
            }
        }
    }

    private async Task ConectarEConsumirAsync(CancellationToken cancellationToken)
    {
        var fabrica = new ConnectionFactory
        {
            HostName = _opcoes.Host,
            Port = _opcoes.Porta,
            UserName = _opcoes.Usuario,
            Password = _opcoes.Senha,
            VirtualHost = _opcoes.VirtualHost,
            AutomaticRecoveryEnabled = true,
        };

        _conexao = await fabrica.CreateConnectionAsync(cancellationToken);
        _canal = await _conexao.CreateChannelAsync(cancellationToken: cancellationToken);

        // Limita mensagens em voo: sem isso o broker despeja a fila inteira na memória.
        await _canal.BasicQosAsync(0, _opcoes.Prefetch, false, cancellationToken);

        var consumidor = new AsyncEventingBasicConsumer(_canal);
        consumidor.ReceivedAsync += ProcessarAsync;

        await _canal.BasicConsumeAsync(
            queue: _opcoes.Fila,
            autoAck: false,
            consumer: consumidor,
            cancellationToken: cancellationToken);

        LogConsumindo(logger, _opcoes.Fila);

        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    private async Task ProcessarAsync(object sender, BasicDeliverEventArgs args)
    {
        var canal = _canal!;

        LancamentoRealizado? evento;

        try
        {
            evento = JsonSerializer.Deserialize<LancamentoRealizado>(
                Encoding.UTF8.GetString(args.Body.Span), Json);
        }
        catch (Exception ex)
        {
            LogPayloadInvalido(logger, args.DeliveryTag, ex);
            evento = null;
        }

        // Sem requeue: mensagem malformada nunca vai melhorar sozinha. Vai para a DLQ.
        if (evento is null || !evento.EhValido())
        {
            await canal.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false);
            return;
        }

        try
        {
            using var escopo = escopos.CreateScope();
            var repositorio = escopo.ServiceProvider.GetRequiredService<ISaldoDiarioRepository>();

            var aplicado = await repositorio.AplicarAsync(evento, _parada);

            if (!aplicado)
            {
                LogDuplicado(logger, evento.EventId);
            }
            else
            {
                var lag = DateTimeOffset.UtcNow - evento.CriadoEm;
                LogAplicado(logger, evento.EventId, evento.CorrelationId, lag.TotalMilliseconds);
            }

            await canal.BasicAckAsync(args.DeliveryTag, multiple: false);
        }
        catch (OperationCanceledException) when (_parada.IsCancellationRequested)
        {
            // Encerrando: nem ack nem nack. A entrega fica pendente e o broker
            // reentrega na volta — o dedupe por eventId cobre a repetição.
        }
        catch (Exception ex)
        {
            LogFalhaAoAplicar(logger, evento.EventId, ex);

            await EsperarEDevolverAsync(canal, args.DeliveryTag);
        }
    }

    /// <summary>
    /// Devolve a mensagem para a fila: a falha é do ambiente, não dela. O delay
    /// evita laço quente; o token impede que ele segure o shutdown por
    /// <c>IntervaloReconexao</c> vezes o número de mensagens em voo.
    /// </summary>
    private async Task EsperarEDevolverAsync(IChannel canal, ulong deliveryTag)
    {
        try
        {
            await Task.Delay(_opcoes.IntervaloReconexao, _parada);
            await canal.BasicNackAsync(deliveryTag, multiple: false, requeue: true);
        }
        catch (OperationCanceledException)
        {
            // Encerrando durante a espera: o broker reentrega na volta.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        if (_canal is not null)
        {
            await _canal.DisposeAsync();
        }

        if (_conexao is not null)
        {
            await _conexao.DisposeAsync();
        }
    }

    [LoggerMessage(EventId = 3000, Level = LogLevel.Information, Message = "Consumindo a fila {Fila}")]
    private static partial void LogConsumindo(ILogger logger, string fila);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning, Message = "Falha ao conectar no broker; tentando de novo")]
    private static partial void LogConexaoFalhou(ILogger logger, Exception excecao);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Error, Message = "Payload inválido na entrega {DeliveryTag}; enviando para a DLQ")]
    private static partial void LogPayloadInvalido(ILogger logger, ulong deliveryTag, Exception excecao);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Debug, Message = "Evento {EventId} já processado; ignorado")]
    private static partial void LogDuplicado(ILogger logger, Guid eventId);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Information, Message = "Evento {EventId} aplicado (correlation {CorrelationId}, lag {LagMs} ms)")]
    private static partial void LogAplicado(ILogger logger, Guid eventId, Guid correlationId, double lagMs);

    [LoggerMessage(EventId = 3005, Level = LogLevel.Error, Message = "Falha ao aplicar o evento {EventId}; devolvendo para a fila")]
    private static partial void LogFalhaAoAplicar(ILogger logger, Guid eventId, Exception excecao);
}
