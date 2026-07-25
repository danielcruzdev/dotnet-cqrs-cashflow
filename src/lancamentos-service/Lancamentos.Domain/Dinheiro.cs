using System.Globalization;

namespace Lancamentos.Domain;

/// <summary>
/// Quantia monetária: um valor estritamente positivo associado a uma moeda.
/// </summary>
/// <remarks>
/// <para>
/// O tipo existe para tornar <b>impossível de representar</b> o estado inválido.
/// Um <c>decimal</c> solto permite valor negativo, permite somar reais com
/// dólares e permite 0,005 entrar no livro-caixa; um <see cref="Dinheiro"/> não.
/// </para>
/// <para>
/// <b>Sempre positivo, por construção.</b> O sinal contábil da operação vem do
/// <see cref="TipoLancamento"/>, nunca do valor. Isso elimina de uma vez a classe
/// de bug "esqueci de negativar" no cálculo do saldo — que é o defeito mais comum
/// em sistema de fluxo de caixa.
/// </para>
/// <para>
/// <c>decimal</c>, nunca <c>double</c>: base 10 é exata para as frações que
/// dinheiro usa. Em ponto flutuante binário, 0,1 + 0,2 não é 0,3.
/// </para>
/// </remarks>
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
    /// <remarks>
    /// Usa a escala declarada do <c>decimal</c> (quarto bloco de bits) e desconta
    /// os zeros à direita: 10.50m tem escala 2, mas 10.500m também é aceito porque
    /// o terceiro dígito é zero e não representa precisão real.
    /// </remarks>
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
