using Lancamentos.Domain;

namespace Lancamentos.Application.CriarLancamento;

/// <summary>Desfecho possível de uma tentativa de criação.</summary>
public enum StatusCriacao
{
    /// <summary>Lançamento novo, gravado agora. A API responde <c>201 Created</c>.</summary>
    Criado = 1,

    /// <summary>
    /// A chave já havia sido usada com o mesmo conteúdo — retry legítimo.
    /// A API responde <c>200 OK</c> com o lançamento original.
    /// </summary>
    JaRegistrado = 2,

    /// <summary>
    /// A chave já havia sido usada com conteúdo diferente. A API responde
    /// <c>409 Conflict</c>.
    /// </summary>
    ConflitoDeChave = 3,
}

/// <summary>Resultado do caso de uso de criação de lançamento.</summary>
public sealed record ResultadoCriarLancamento(StatusCriacao Status, Lancamento Lancamento)
{
    public static ResultadoCriarLancamento Criado(Lancamento lancamento) =>
        new(StatusCriacao.Criado, lancamento);

    public static ResultadoCriarLancamento JaRegistrado(Lancamento lancamento) =>
        new(StatusCriacao.JaRegistrado, lancamento);

    public static ResultadoCriarLancamento ConflitoDeChave(Lancamento existente) =>
        new(StatusCriacao.ConflitoDeChave, existente);
}
