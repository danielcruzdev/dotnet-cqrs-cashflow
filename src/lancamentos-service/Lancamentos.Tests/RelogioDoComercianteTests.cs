using FluentAssertions;
using Lancamentos.Infrastructure.Tempo;

namespace Lancamentos.Tests;

public class RelogioDoComercianteTests
{
    [Fact]
    public void HojeSegueOFusoDoComercianteENaoOUtc()
    {
        // 01h UTC do dia 27 é 22h do dia 26 em São Paulo.
        var relogio = new RelogioDoComerciante(
            new TempoFixo(new DateTimeOffset(2026, 7, 27, 1, 0, 0, TimeSpan.Zero)));

        relogio.HojeParaComerciante.Should().Be(new DateOnly(2026, 7, 26));
        relogio.AgoraUtc.Should().Be(new DateTimeOffset(2026, 7, 27, 1, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void FusoInvalidoFalhaAoConstruirENaoNaPrimeiraRequisicao()
    {
        var acao = () => new RelogioDoComerciante(new TempoFixo(DateTimeOffset.UnixEpoch), "Fuso/Inexistente");

        acao.Should().Throw<TimeZoneNotFoundException>();
    }
}
