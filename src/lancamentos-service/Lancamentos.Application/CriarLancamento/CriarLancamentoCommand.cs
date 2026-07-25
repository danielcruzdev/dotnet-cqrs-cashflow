using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lancamentos.Application.Abstracoes;

namespace Lancamentos.Application.CriarLancamento;

/// <summary>Registrar um novo lançamento no livro-caixa.</summary>
public sealed record CriarLancamentoCommand : ICommand<ResultadoCriarLancamento>
{
    public required Guid ComercianteId { get; init; }

    /// <summary>"DEBITO" ou "CREDITO", como chega da API.</summary>
    public required string Tipo { get; init; }

    public required decimal Valor { get; init; }

    public required string Moeda { get; init; }

    public required DateOnly DataCompetencia { get; init; }

    public string? Descricao { get; init; }

    public required string ChaveIdempotencia { get; init; }

    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// SHA-256 do conteúdo de negócio do comando, em hexadecimal minúsculo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// O hash é calculado sobre os <b>campos canonicalizados</b>, não sobre os
    /// bytes brutos do corpo HTTP. A diferença importa: assim, dois retries que
    /// diferem apenas em espaçamento do JSON ou ordem das propriedades contam
    /// como o mesmo payload, enquanto uma mudança de valor conta como payload
    /// diferente. "Mesmo payload" passa a significar "mesma intenção de
    /// negócio", que é o que a idempotência realmente quer dizer.
    /// </para>
    /// <para>
    /// A <see cref="ChaveIdempotencia"/> e o <see cref="CorrelationId"/> ficam
    /// deliberadamente <b>fora</b> do hash: a chave é o identificador da
    /// operação, não parte dela, e o correlation id muda a cada tentativa.
    /// Incluí-los faria todo retry parecer um payload novo.
    /// </para>
    /// </remarks>
    public string CalcularHashPayload()
    {
        var canonico = string.Create(CultureInfo.InvariantCulture,
            $"{ComercianteId:N}|{Tipo.Trim().ToUpperInvariant()}|{Valor:0.00}|" +
            $"{Moeda.Trim().ToUpperInvariant()}|{DataCompetencia:yyyy-MM-dd}|{Descricao?.Trim() ?? string.Empty}");

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonico));

        return Convert.ToHexStringLower(hash);
    }
}
