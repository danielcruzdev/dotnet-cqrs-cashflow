using Lancamentos.Domain;

namespace Lancamentos.Tests;

/// <summary>Relógio de teste: o "hoje" do comerciante é dito, não calculado.</summary>
internal sealed class RelogioFalso(DateTimeOffset agoraUtc, DateOnly hojeParaComerciante) : IRelogio
{
    public DateTimeOffset AgoraUtc { get; } = agoraUtc;

    public DateOnly HojeParaComerciante { get; } = hojeParaComerciante;
}

internal sealed class TempoFixo(DateTimeOffset agora) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => agora;
}
