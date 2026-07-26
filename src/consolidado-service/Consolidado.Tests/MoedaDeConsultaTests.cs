using Consolidado.Domain;
using FluentAssertions;

namespace Consolidado.Tests;

public class MoedaDeConsultaTests
{
    [Theory]
    [InlineData("BRL", "BRL")]
    [InlineData("brl", "BRL")]
    [InlineData("  usd ", "USD")]
    public void CodigoValidoEhNormalizado(string entrada, string esperado) =>
        MoedaDeConsulta.Normalizar(entrada).Should().Be(esperado);

    // Sem validação, "?moeda=zzzzz" devolveria 200 com zeros e criaria chave inútil no cache.
    [Theory]
    [InlineData("zzzzz")]
    [InlineData("BR")]
    [InlineData("B1L")]
    [InlineData("")]
    [InlineData(null)]
    public void CodigoInvalidoEhRejeitado(string? entrada)
    {
        var acao = () => MoedaDeConsulta.Normalizar(entrada);

        acao.Should().Throw<ConsultaInvalidaException>();
    }
}
