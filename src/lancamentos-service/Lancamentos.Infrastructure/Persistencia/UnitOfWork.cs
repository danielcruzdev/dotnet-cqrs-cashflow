using Lancamentos.Domain.Abstracoes;

namespace Lancamentos.Infrastructure.Persistencia;

/// <inheritdoc cref="IUnitOfWork"/>
public sealed class UnitOfWork(SessaoDeBanco sessao) : IUnitOfWork
{
    public async Task<T> ExecutarAsync<T>(
        Func<CancellationToken, Task<T>> operacao,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operacao);

        // Chamada aninhada participa da transação externa.
        if (sessao.EmTransacao)
        {
            return await operacao(cancellationToken);
        }

        await sessao.IniciarTransacaoAsync(cancellationToken);

        try
        {
            var resultado = await operacao(cancellationToken);
            await sessao.ConfirmarAsync(cancellationToken);
            return resultado;
        }
        catch
        {
            // None de propósito: token cancelado abortaria o próprio rollback.
            await sessao.DesfazerAsync(CancellationToken.None);
            throw;
        }
    }

    public Task ExecutarAsync(
        Func<CancellationToken, Task> operacao,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operacao);

        return ExecutarAsync(async ct =>
        {
            await operacao(ct);
            return true;
        }, cancellationToken);
    }
}
