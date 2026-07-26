using FluentAssertions;
using Lancamentos.Domain;

namespace Lancamentos.Tests;

public class MoedaTests
{
    [Theory]
    [InlineData("BRL")]
    [InlineData("USD")]
    [InlineData("EUR")]
    public void CodigoSuportadoEhAceito(string codigo) =>
        Moeda.Criar(codigo).Codigo.Should().Be(codigo);

    [Theory]
    [InlineData("brl", "BRL")]
    [InlineData("  usd  ", "USD")]
    public void CodigoEhNormalizado(string entrada, string esperado) =>
        Moeda.Criar(entrada).Codigo.Should().Be(esperado);

    [Theory]
    [InlineData("XYZ")]
    [InlineData("BR")]
    [InlineData("")]
    [InlineData(null)]
    public void CodigoNaoSuportadoEhRejeitado(string? codigo)
    {
        var acao = () => Moeda.Criar(codigo);

        acao.Should().Throw<DominioException>()
            .Which.Codigo.Should().Be("lancamento.moeda_nao_suportada");
    }

    [Fact]
    public void TentarCriarNaoLancaParaCodigoInvalido() =>
        Moeda.TentarCriar("XYZ", out _).Should().BeFalse();
}
