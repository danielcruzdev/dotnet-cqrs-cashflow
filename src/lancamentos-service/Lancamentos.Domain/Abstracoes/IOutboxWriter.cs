using Lancamentos.Domain.Eventos;

namespace Lancamentos.Domain.Abstracoes;

/// <summary>
/// Grava um evento na outbox transacional.
/// </summary>
/// <remarks>
/// <para>
/// A escrita acontece na <b>mesma transação local</b> do lançamento que a
/// originou. É isso que garante a invariante central do sistema: todo lançamento
/// aceito tem exatamente um evento pendente de publicação — ou os dois vão, ou
/// nenhum vai. Sem transação distribuída e sem 2PC.
/// </para>
/// <para>
/// A interface recebe o <see cref="EventoDeDominio"/> em vez de uma string JSON
/// de propósito: serialização é decisão de infraestrutura, e o domínio não deve
/// conhecer o formato de transporte.
/// </para>
/// </remarks>
public interface IOutboxWriter
{
    Task EscreverAsync(EventoDeDominio evento, CancellationToken cancellationToken = default);
}
