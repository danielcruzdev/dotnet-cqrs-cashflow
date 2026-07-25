using System.Globalization;

namespace Lancamentos.Domain;

/// <summary>
/// Quantia monetária: um valor estritamente positivo associado a uma moeda.
/// </summary>
public readonly record struct Dinheiro
{
    public decimal Valor { get; }

    public Moeda Moeda { get; }

    private Dinheiro(decimal valor, Moeda moeda)
    {
        Valor = valor;
        Moeda = moeda;
    }

    /// <exception cref="DominioException">
    /// Se o valor não for positivo ou tiver mais casas decimais do que a moeda comporta.
    /// </exception>
    public static Dinheiro Criar(decimal valor, Moeda moeda)
    {
        if (valor <= 0m)
        {
            throw DominioException.ValorNaoPositivo(valor);
        }

        if (CasasDecimaisDe(valor) > Moeda.CasasDecimais)
        {
            // Rejeitar em vez de arredondar é deliberado. Arredondamento
            // silencioso em livro-razão vira divergência acumulada que só
            // aparece na conciliação, quando já é caro de rastrear.
            throw DominioException.PrecisaoMonetariaInvalida(valor, Moeda.CasasDecimais);
        }

        return new Dinheiro(valor, moeda);
    }

    public static Dinheiro EmReais(decimal valor) => Criar(valor, Moeda.Brl);

    /// <summary>
    /// Escala efetiva do decimal — quantas casas decimais o valor de fato ocupa.
    /// </summary>
    private static int CasasDecimaisDe(decimal valor)
    {
        var escala = (byte)(decimal.GetBits(valor)[3] >> 16);

        while (escala > 0 && valor == decimal.Round(valor, escala - 1))
        {
            escala--;
        }

        return escala;
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Valor:0.00} {Moeda.Codigo}");
}
