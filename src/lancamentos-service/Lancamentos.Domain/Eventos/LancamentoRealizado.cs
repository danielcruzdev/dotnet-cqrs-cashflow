using System.Text.Json.Serialization;

namespace Lancamentos.Domain.Eventos;

/// <summary>
/// Publicado a cada lançamento registrado. Estorno não tem evento próprio: é um
/// LancamentoRealizado com tipo invertido e EstornoDeId preenchido.
/// </summary>
public sealed record LancamentoRealizado : EventoDeDominio
{
    public override int Version => 1;

    public override string EventType => nameof(LancamentoRealizado);

    [JsonIgnore]
    public override string RoutingKey => "lancamento.realizado.v1";

    public required Guid LancamentoId { get; init; }

    public required string Tipo { get; init; }

    public required decimal Valor { get; init; }

    public required string Moeda { get; init; }

    public Guid? EstornoDeId { get; init; }

    /// <summary>Instante da escrita na origem. Base do SLI de lag de consistência.</summary>
    public required DateTimeOffset CriadoEm { get; init; }

    public static LancamentoRealizado De(Lancamento lancamento, Guid correlationId)
    {
        ArgumentNullException.ThrowIfNull(lancamento);

        return new LancamentoRealizado
        {
            OccurredAt = lancamento.CriadoEm,
            CorrelationId = correlationId,
            AgregadoId = lancamento.Id,
            ComercianteId = lancamento.ComercianteId,
            DataCompetencia = lancamento.DataCompetencia,
            LancamentoId = lancamento.Id,
            Tipo = lancamento.Tipo.ParaPersistencia(),
            Valor = lancamento.Valor.Valor,
            Moeda = lancamento.Valor.Moeda.Codigo,
            EstornoDeId = lancamento.EstornoDeId,
            CriadoEm = lancamento.CriadoEm,
        };
    }
}
