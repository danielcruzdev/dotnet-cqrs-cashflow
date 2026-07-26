namespace Consolidado.Domain;

/// <summary>Projeção do saldo de um comerciante em um dia e uma moeda.</summary>
public sealed record SaldoDiario
{
    public Guid ComercianteId { get; init; }
    public DateOnly Data { get; init; }
    public string Moeda { get; init; } = string.Empty;
    public decimal TotalDebitos { get; init; }
    public decimal TotalCreditos { get; init; }
    public decimal Saldo { get; init; }
    public int QtdLancamentos { get; init; }
    public DateTimeOffset? AtualizadoEm { get; init; }

    /// <summary>Dia sem lançamentos: zeros, não erro.</summary>
    public static SaldoDiario Vazio(Guid comercianteId, DateOnly data, string moeda) => new()
    {
        ComercianteId = comercianteId,
        Data = data,
        Moeda = moeda,
    };
}

/// <summary>Quanto um lançamento move em cada coluna da projeção.</summary>
public readonly record struct Movimento(decimal Debito, decimal Credito, decimal Saldo)
{
    public static Movimento De(string tipo, decimal valor) => tipo switch
    {
        "CREDITO" => new Movimento(0, valor, valor),
        "DEBITO" => new Movimento(valor, 0, -valor),
        _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, "Tipo de lançamento desconhecido."),
    };
}
