namespace Lancamentos.Domain.Abstracoes;

/// <summary>
/// Executa um bloco de operações dentro de uma única transação de banco.
/// </summary>
/// <remarks>
/// <para>
/// A interface expõe <b>um único método que envolve a operação</b>, em vez do
/// trio <c>Begin</c>/<c>Commit</c>/<c>Rollback</c>. A diferença é que aqui é
/// impossível esquecer o commit ou vazar uma transação aberta: ou o delegate
/// completa e a transação é confirmada, ou uma exceção sobe e ela é desfeita.
/// </para>
/// <para>
/// Isso importa mais neste sistema do que no caso geral, porque a atomicidade
/// entre o lançamento e a linha da outbox é justamente o que sustenta a garantia
/// de que nenhum evento se perde.
/// </para>
/// </remarks>
public interface IUnitOfWork
{
    Task<T> ExecutarAsync<T>(
        Func<CancellationToken, Task<T>> operacao,
        CancellationToken cancellationToken = default);

    Task ExecutarAsync(
        Func<CancellationToken, Task> operacao,
        CancellationToken cancellationToken = default);
}
