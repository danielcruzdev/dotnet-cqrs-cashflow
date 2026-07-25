using System.Collections.Frozen;

namespace Lancamentos.Domain;

/// <summary>
/// Código de moeda ISO 4217, restrito ao conjunto que o sistema realmente suporta.
/// </summary>
/// <remarks>
/// <para>
/// A lista é deliberadamente uma <b>allowlist curta</b>, não a tabela ISO 4217
/// completa. Duas razões:
/// </para>
/// <list type="number">
///   <item>
///     Aceitar um código que o sistema não sabe tratar é uma promessa falsa. O
///     saldo consolidado é segregado por moeda e não há conversão cambial no
///     escopo — então "suportar" 180 moedas significaria apenas acumular saldos
///     isolados que ninguém consegue somar.
///   </item>
///   <item>
///     Todas as moedas aqui têm <b>duas casas decimais</b>. O armazenamento é
///     <c>NUMERIC(18,2)</c>, então aceitar JPY (zero casas) ou KWD (três) criaria
///     divergência silenciosa entre o que o cliente envia e o que é persistido.
///   </item>
/// </list>
/// <para>
/// Ampliar o suporte é uma decisão de produto que exige mexer no schema e definir
/// política de câmbio — não é adicionar uma string nesta lista.
/// </para>
/// </remarks>
public readonly record struct Moeda
{
    /// <summary>Moedas ISO 4217 suportadas — todas com duas casas decimais.</summary>
    public static readonly FrozenSet<string> Suportadas = new[]
    {
        "BRL", // real brasileiro — moeda padrão do sistema
        "USD", "EUR", "GBP", "CHF", "CAD", "AUD",
        "ARS", "COP", "MXN", "UYU",
        "CNY", "INR", "ZAR",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Número de casas decimais suportadas, alinhado ao <c>NUMERIC(18,2)</c>.</summary>
    public const int CasasDecimais = 2;

    public string Codigo { get; }

    private Moeda(string codigo) => Codigo = codigo;

    /// <summary>Real brasileiro — default do sistema.</summary>
    public static Moeda Brl { get; } = new("BRL");

    /// <exception cref="DominioException">Se o código não for suportado.</exception>
    public static Moeda Criar(string? codigo)
    {
        if (!TentarCriar(codigo, out var moeda))
        {
            throw DominioException.MoedaNaoSuportada(codigo ?? "(vazio)");
        }

        return moeda;
    }

    public static bool TentarCriar(string? codigo, out Moeda moeda)
    {
        moeda = default;

        if (string.IsNullOrWhiteSpace(codigo))
        {
            return false;
        }

        // Normaliza com ToUpperInvariant: o código é um identificador técnico,
        // não texto de usuário. ToUpper() dependeria da cultura corrente e
        // produziria resultado diferente em locale turco (o "problema do i sem
        // ponto"), que é justamente o tipo de bug que só aparece em produção.
        var normalizado = codigo.Trim().ToUpperInvariant();

        if (!Suportadas.Contains(normalizado))
        {
            return false;
        }

        moeda = new Moeda(normalizado);
        return true;
    }

    public override string ToString() => Codigo;

    public static implicit operator string(Moeda moeda) => moeda.Codigo;
}
