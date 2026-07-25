namespace Lancamentos.Application.Abstracoes;

/// <summary>Intenção de alterar o estado do sistema.</summary>
public interface ICommand<TResultado>;

/// <summary>Executa um <see cref="ICommand{TResultado}"/>.</summary>
public interface ICommandHandler<in TCommand, TResultado>
    where TCommand : ICommand<TResultado>
{
    Task<TResultado> ExecutarAsync(TCommand comando, CancellationToken cancellationToken = default);
}
