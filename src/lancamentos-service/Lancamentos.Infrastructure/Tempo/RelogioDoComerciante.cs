using Lancamentos.Domain;

namespace Lancamentos.Infrastructure.Tempo;

/// <inheritdoc cref="IRelogio"/>
public sealed class RelogioDoComerciante : IRelogio
{
    /// <summary>
    /// Fuso que define o dia contábil.
    /// </summary>
    public const string FusoPadrao = "America/Sao_Paulo";

    private readonly TimeProvider _tempo;
    private readonly TimeZoneInfo _fuso;

    public RelogioDoComerciante(TimeProvider tempo, string? fusoHorario = null)
    {
        ArgumentNullException.ThrowIfNull(tempo);

        _tempo = tempo;
        _fuso = TimeZoneInfo.FindSystemTimeZoneById(
            string.IsNullOrWhiteSpace(fusoHorario) ? FusoPadrao : fusoHorario);
    }

    public DateTimeOffset AgoraUtc => _tempo.GetUtcNow();

    public DateOnly HojeParaComerciante =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_tempo.GetUtcNow(), _fuso).DateTime);
}
