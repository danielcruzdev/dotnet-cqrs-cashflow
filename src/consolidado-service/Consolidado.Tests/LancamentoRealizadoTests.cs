using Consolidado.Domain;
using FluentAssertions;

namespace Consolidado.Tests;

public class LancamentoRealizadoTests
{
    private static LancamentoRealizado Evento() => new()
    {
        EventId = Guid.NewGuid(),
        Version = 1,
        CorrelationId = Guid.NewGuid(),
        ComercianteId = Guid.NewGuid(),
        DataCompetencia = new DateOnly(2026, 7, 26),
        LancamentoId = Guid.NewGuid(),
        Tipo = "CREDITO",
        Valor = 100m,
        Moeda = "BRL",
        CriadoEm = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void EventoCompletoEhValido() => Evento().EhValido().Should().BeTrue();

    // Evento inválido vai para a DLQ sem requeue: não melhora sozinho.
    [Fact]
    public void EventoSemIdEhInvalido() =>
        (Evento() with { EventId = Guid.Empty }).EhValido().Should().BeFalse();

    [Fact]
    public void EventoSemComercianteEhInvalido() =>
        (Evento() with { ComercianteId = Guid.Empty }).EhValido().Should().BeFalse();

    [Theory]
    [InlineData("TRANSFERENCIA")]
    [InlineData("")]
    public void TipoDesconhecidoEhInvalido(string tipo) =>
        (Evento() with { Tipo = tipo }).EhValido().Should().BeFalse();

    [Fact]
    public void ValorNaoPositivoEhInvalido() =>
        (Evento() with { Valor = 0m }).EhValido().Should().BeFalse();

    [Fact]
    public void MoedaForaDoPadraoIsoEhInvalida() =>
        (Evento() with { Moeda = "REAL" }).EhValido().Should().BeFalse();
}
