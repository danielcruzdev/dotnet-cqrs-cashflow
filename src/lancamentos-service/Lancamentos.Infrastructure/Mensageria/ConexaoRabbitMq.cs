using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Lancamentos.Infrastructure.Mensageria;

/// <summary>
/// Dona da conexão e do canal com o broker.
/// </summary>
public sealed class ConexaoRabbitMq(IOptions<RabbitMqOptions> opcoes) : IAsyncDisposable
{
    private readonly RabbitMqOptions _opcoes = opcoes.Value;
    private readonly SemaphoreSlim _exclusao = new(1, 1);

    private IConnection? _conexao;
    private IChannel? _canal;

    public async Task<IChannel> ObterCanalAsync(CancellationToken cancellationToken)
    {
        if (_canal is { IsOpen: true })
        {
            return _canal;
        }

        await _exclusao.WaitAsync(cancellationToken);

        try
        {
            if (_canal is { IsOpen: true })
            {
                return _canal;
            }

            await DescartarAsync();

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

            // Sem confirmação, "publicado" significaria apenas "enviado".
            var configuracao = new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true);

            _canal = await _conexao.CreateChannelAsync(configuracao, cancellationToken);

            return _canal;
        }
        finally
        {
            _exclusao.Release();
        }
    }

    private async Task DescartarAsync()
    {
        if (_canal is not null)
        {
            await _canal.DisposeAsync();
            _canal = null;
        }

        if (_conexao is not null)
        {
            await _conexao.DisposeAsync();
            _conexao = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DescartarAsync();
        _exclusao.Dispose();
    }
}
