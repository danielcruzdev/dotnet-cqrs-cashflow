using Lancamentos.Domain;

namespace Lancamentos.Infrastructure.Tempo;

/// <inheritdoc cref="IRelogio"/>
public sealed class RelogioDoComerciante : IRelogio
{
    /// <summary>
    /// Fuso que define o dia contábil.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Resolvido por ID IANA. Isso funciona também no Windows porque o .NET
    /// converte entre IDs IANA e Windows usando dados de ICU — o que exige
    /// <c>InvariantGlobalization=false</c>, fixado no <c>Directory.Build.props</c>
    /// justamente por causa desta linha.
    /// </para>
    /// <para>
    /// O fuso é configurável para não amarrar o sistema a um único país: a
    /// regra de negócio é "o dia civil onde o comerciante opera", e São Paulo é
    /// apenas o default.
    /// </para>
    /// </remarks>
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
