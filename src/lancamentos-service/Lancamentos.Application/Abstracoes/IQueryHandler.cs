namespace Lancamentos.Application.Abstracoes;

/// <summary>Intenção de ler estado, sem efeito colateral.</summary>
public interface IQuery<TResultado>;

public interface IQueryHandler<in TQuery, TResultado>
    where TQuery : IQuery<TResultado>
{
    Task<TResultado> ExecutarAsync(TQuery consulta, CancellationToken cancellationToken = default);
}
