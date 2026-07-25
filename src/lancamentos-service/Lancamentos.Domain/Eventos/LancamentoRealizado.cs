namespace Lancamentos.Domain.Eventos;

/// <summary>
/// Publicado sempre que um lançamento é registrado. É o único elo entre o
/// serviço de Lançamentos e o de Consolidado.
/// </summary>
public sealed record LancamentoRealizado : EventoDeDominio
{
    public override int Version => 1;

    public override string EventType => nameof(LancamentoRealizado);

    public override string RoutingKey => "lancamento.realizado.v1";

    public required Guid LancamentoId { get; init; }

    public required Guid Comerciante { get; init; }

    public required string Tipo { get; init; }

    public required decimal Valor { get; init; }

    public required string Moeda { get; init; }

    public required DateOnly Competencia { get; init; }

    public Guid? EstornoDeId { get; init; }

    /// <summary>
    /// Instante em que o lançamento foi gravado no serviço de origem.
    /// </summary>
    public required DateTimeOffset CriadoEm { get; init; }

    public override Guid AgregadoId => LancamentoId;

    public override Guid ComercianteId => Comerciante;

    public override DateOnly DataCompetencia => Competencia;

    /// <summary>Monta o evento a partir da entidade recém-criada.</summary>
    public static LancamentoRealizado De(Lancamento lancamento, Guid correlationId)
    {
        ArgumentNullException.ThrowIfNull(lancamento);

        return new LancamentoRealizado
        {
            OccurredAt = lancamento.CriadoEm,
            CorrelationId = correlationId,
            LancamentoId = lancamento.Id,
            Comerciante = lancamento.ComercianteId,
            Tipo = lancamento.Tipo.ParaPersistencia(),
            Valor = lancamento.Valor.Valor,
            Moeda = lancamento.Valor.Moeda.Codigo,
            Competencia = lancamento.DataCompetencia,
            EstornoDeId = lancamento.EstornoDeId,
            CriadoEm = lancamento.CriadoEm,
        };
    }
}
