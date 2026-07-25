namespace Lancamentos.Domain.Abstracoes;

/// <summary>
/// A chave de idempotência já está em uso por outro lançamento do mesmo comerciante.
/// </summary>
/// <remarks>
/// <para>
/// Existe para cobrir a corrida entre a consulta e a gravação. O caso de uso
/// primeiro procura pela chave e só então insere — entre as duas operações há
/// uma janela em que outra requisição concorrente com a mesma chave pode
/// inserir primeiro. Nesse instante a constraint única do banco é a única
/// autoridade real, e é ela que decide.
/// </para>
/// <para>
/// A infraestrutura traduz a violação de constraint nesta exceção para que a
/// camada de aplicação possa tratá-la como <b>caso esperado</b> (reler e
/// devolver o lançamento vencedor), e não como falha. Sem essa tradução, a
/// aplicação precisaria conhecer o <c>SqlState</c> do PostgreSQL — detalhe de
/// infraestrutura vazando para dentro do caso de uso.
/// </para>
/// </remarks>
public sealed class ChaveIdempotenciaEmUsoException(Guid comercianteId, string chave, Exception? innerException = null)
    : Exception($"A chave de idempotência '{chave}' já está em uso pelo comerciante {comercianteId}.", innerException)
{
    public Guid ComercianteId { get; } = comercianteId;

    public string Chave { get; } = chave;
}
