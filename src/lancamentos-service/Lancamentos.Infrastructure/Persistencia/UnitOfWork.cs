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

        // Chamada aninhada participa da transação externa em vez de abrir uma
        // nova. Postgres não tem transação aninhada de verdade (só SAVEPOINT), e
        // fingir que tem produziria commits parciais silenciosos.
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
            // Rollback com CancellationToken.None de propósito: se o cancelamento
            // foi justamente o motivo da falha, passar o token cancelado abortaria
            // o próprio rollback e deixaria a transação pendurada até o timeout.
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
