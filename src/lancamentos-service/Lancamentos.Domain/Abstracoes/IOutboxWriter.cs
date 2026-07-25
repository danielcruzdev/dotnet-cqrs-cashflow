using Lancamentos.Domain.Eventos;

namespace Lancamentos.Domain.Abstracoes;

/// <summary>
/// Grava um evento na outbox transacional.
/// </summary>
public interface IOutboxWriter
{
    Task EscreverAsync(EventoDeDominio evento, CancellationToken cancellationToken = default);
}
