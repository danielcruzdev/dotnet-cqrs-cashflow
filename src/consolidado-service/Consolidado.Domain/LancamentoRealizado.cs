namespace Consolidado.Domain;

/// <summary>
/// Contrato do evento consumido. Cópia deliberada do contrato do produtor: uma
/// biblioteca compartilhada acoplaria o deploy dos dois serviços.
/// </summary>
public sealed record LancamentoRealizado
{
    public Guid EventId { get; init; }
    public int Version { get; init; }
    public Guid CorrelationId { get; init; }
    public Guid ComercianteId { get; init; }
    public DateOnly DataCompetencia { get; init; }
    public Guid LancamentoId { get; init; }
    public string Tipo { get; init; } = string.Empty;
    public decimal Valor { get; init; }
    public string Moeda { get; init; } = string.Empty;
    public DateTimeOffset CriadoEm { get; init; }

    public bool EhValido() =>
        EventId != Guid.Empty
        && ComercianteId != Guid.Empty
        && Valor > 0
        && Tipo is "DEBITO" or "CREDITO"
        && Moeda.Length == 3;
}
