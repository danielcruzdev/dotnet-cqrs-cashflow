namespace Lancamentos.Domain.Abstracoes;

/// <summary>
/// Executa um bloco de operações dentro de uma única transação de banco.
/// </summary>
public interface IUnitOfWork
{
    Task<T> ExecutarAsync<T>(
        Func<CancellationToken, Task<T>> operacao,
        CancellationToken cancellationToken = default);

    Task ExecutarAsync(
        Func<CancellationToken, Task> operacao,
        CancellationToken cancellationToken = default);
}
