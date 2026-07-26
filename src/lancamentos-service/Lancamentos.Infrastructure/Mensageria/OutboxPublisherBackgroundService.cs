using System.Text;
using Lancamentos.Domain.Abstracoes;
using Lancamentos.Infrastructure.Persistencia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Lancamentos.Infrastructure.Mensageria;

/// <summary>
/// Lê a outbox e publica os eventos pendentes no broker.
/// </summary>
public sealed partial class OutboxPublisherBackgroundService(
    IServiceScopeFactory escopos,
    ConexaoRabbitMq conexao,
    IOptions<RabbitMqOptions> opcoes,
    ILogger<OutboxPublisherBackgroundService> logger) : BackgroundService
{
    private readonly RabbitMqOptions _opcoes = opcoes.Value;
    private int _falhasConsecutivas;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogIniciado(logger, _opcoes.Exchange);

        while (!stoppingToken.IsCancellationRequested)
        {
            var publicadas = 0;

            try
            {
                publicadas = await ProcessarLoteAsync(stoppingToken);
                _falhasConsecutivas = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _falhasConsecutivas++;
                LogCicloFalhou(logger, _falhasConsecutivas, ex);
            }

            // Lote cheio provavelmente tem mais pendências: não espera.
            if (publicadas < _opcoes.TamanhoLote)
            {
                await EsperarAsync(stoppingToken);
            }
        }

        LogEncerrado(logger);
    }

    private async Task<int> ProcessarLoteAsync(CancellationToken cancellationToken)
    {
        await using var escopo = escopos.CreateAsyncScope();

        var outbox = escopo.ServiceProvider.GetRequiredService<OutboxRepository>();
        var unitOfWork = escopo.ServiceProvider.GetRequiredService<IUnitOfWork>();

        return await unitOfWork.ExecutarAsync(async ct =>
        {
            var pendentes = await outbox.ReservarPendentesAsync(_opcoes.TamanhoLote, ct);

            if (pendentes.Count == 0)
            {
                return 0;
            }

            // Depois da reserva: broker fora desfaz a transação sem gastar tentativa.
            var canal = await conexao.ObterCanalAsync(ct);

            var publicadas = 0;

            foreach (var mensagem in pendentes)
            {
                if (await PublicarAsync(canal, mensagem, ct))
                {
                    await outbox.MarcarProcessadaAsync(mensagem.Id, ct);
                    publicadas++;
                }
                else
                {
                    await outbox.RegistrarFalhaAsync(mensagem.Id, ct);
                }
            }

            return publicadas;
        }, cancellationToken);
    }

    private async Task<bool> PublicarAsync(
        IChannel canal,
        MensagemOutbox mensagem,
        CancellationToken cancellationToken)
    {
        var routingKey = RoutingKeyDe(mensagem.TipoEvento);

        if (routingKey is null)
        {
            LogTipoDesconhecido(logger, mensagem.TipoEvento, mensagem.Id);
            return false;
        }

        var propriedades = new BasicProperties
        {
            MessageId = mensagem.EventId.ToString(),
            Type = mensagem.TipoEvento,
            ContentType = "application/json",
            ContentEncoding = "utf-8",
            DeliveryMode = DeliveryModes.Persistent,
        };

        try
        {
            await canal.BasicPublishAsync(
                exchange: _opcoes.Exchange,
                routingKey: routingKey,
                mandatory: true,
                basicProperties: propriedades,
                body: Encoding.UTF8.GetBytes(mensagem.Payload),
                cancellationToken: cancellationToken);

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogPublicacaoFalhou(logger, mensagem.EventId, mensagem.Tentativas + 1, ex);
            return false;
        }
    }

    /// <summary>Mapa explícito de tipo de evento para chave de roteamento.</summary>
    private static string? RoutingKeyDe(string tipoEvento) => tipoEvento switch
    {
        "LancamentoRealizado" => "lancamento.realizado.v1",
        _ => null,
    };

    /// <summary>Backoff exponencial sobre falhas consecutivas, com teto.</summary>
    private Task EsperarAsync(CancellationToken cancellationToken)
    {
        var espera = _opcoes.IntervaloVarredura;

        if (_falhasConsecutivas > 0)
        {
            var fator = Math.Pow(2, Math.Min(_falhasConsecutivas, 10));
            var calculada = TimeSpan.FromMilliseconds(espera.TotalMilliseconds * fator);
            espera = calculada > _opcoes.BackoffMaximo ? _opcoes.BackoffMaximo : calculada;
        }

        return Task.Delay(espera, cancellationToken);
    }

    [LoggerMessage(EventId = 2000, Level = LogLevel.Information,
        Message = "Publisher da outbox iniciado. Exchange: {Exchange}")]
    private static partial void LogIniciado(ILogger logger, string exchange);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information,
        Message = "Publisher da outbox encerrado")]
    private static partial void LogEncerrado(ILogger logger);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning,
        Message = "Ciclo do publisher falhou ({FalhasConsecutivas} consecutivas). Aplicando backoff")]
    private static partial void LogCicloFalhou(ILogger logger, int falhasConsecutivas, Exception excecao);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Warning,
        Message = "Falha ao publicar evento {EventId} (tentativa {Tentativa})")]
    private static partial void LogPublicacaoFalhou(ILogger logger, Guid eventId, int tentativa, Exception excecao);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Error,
        Message = "Tipo de evento desconhecido '{TipoEvento}' na mensagem {MensagemId}")]
    private static partial void LogTipoDesconhecido(ILogger logger, string tipoEvento, Guid mensagemId);
}
