namespace Lancamentos.Domain;

/// <summary>
/// Natureza contábil do lançamento. É daqui que vem o sinal da operação.
/// </summary>
public enum TipoLancamento
{
    /// <summary>Saída de caixa. Subtrai do saldo diário.</summary>
    Debito = 1,

    /// <summary>Entrada de caixa. Soma ao saldo diário.</summary>
    Credito = 2,
}

public static class TipoLancamentoExtensions
{
    /// <summary>
    /// Tipo oposto — a base do estorno como lançamento compensatório.
    /// </summary>
    public static TipoLancamento Inverso(this TipoLancamento tipo) => tipo switch
    {
        TipoLancamento.Debito => TipoLancamento.Credito,
        TipoLancamento.Credito => TipoLancamento.Debito,
        _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, "Tipo de lançamento desconhecido."),
    };

    /// <summary>
    /// Multiplicador do valor no cálculo do saldo: +1 para crédito, -1 para débito.
    /// </summary>
    public static int Sinal(this TipoLancamento tipo) => tipo switch
    {
        TipoLancamento.Credito => 1,
        TipoLancamento.Debito => -1,
        _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, "Tipo de lançamento desconhecido."),
    };

    /// <summary>Representação persistida e transportada no evento.</summary>
    public static string ParaPersistencia(this TipoLancamento tipo) => tipo switch
    {
        TipoLancamento.Debito => "DEBITO",
        TipoLancamento.Credito => "CREDITO",
        _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, "Tipo de lançamento desconhecido."),
    };

    public static bool TentarConverter(string? valor, out TipoLancamento tipo)
    {
        switch (valor?.Trim().ToUpperInvariant())
        {
            case "DEBITO":
                tipo = TipoLancamento.Debito;
                return true;
            case "CREDITO":
                tipo = TipoLancamento.Credito;
                return true;
            default:
                tipo = default;
                return false;
        }
    }
}
