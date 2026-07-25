namespace Lancamentos.Application.Abstracoes;

/// <summary>Intenção de alterar o estado do sistema.</summary>
public interface ICommand<TResultado>;

public interface ICommandHandler<in TCommand, TResultado>
    where TCommand : ICommand<TResultado>
{
    Task<TResultado> ExecutarAsync(TCommand comando, CancellationToken cancellationToken = default);
}
