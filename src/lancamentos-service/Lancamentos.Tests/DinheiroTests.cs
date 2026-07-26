using System.Globalization;
using FluentAssertions;
using Lancamentos.Domain;

namespace Lancamentos.Tests;

public class DinheiroTests
{
    // Os valores vêm como texto para que a escala do decimal seja a do literal,
    // e não o que a conversão de double produzir.
    private static decimal Valor(string texto) => decimal.Parse(texto, CultureInfo.InvariantCulture);

    [Fact]
    public void ValorPositivoEhAceito()
    {
        var dinheiro = Dinheiro.EmReais(150.75m);

        dinheiro.Valor.Should().Be(150.75m);
        dinheiro.Moeda.Codigo.Should().Be("BRL");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0.01")]
    [InlineData("-100")]
    public void ValorNaoPositivoEhRejeitado(string texto)
    {
        var acao = () => Dinheiro.EmReais(Valor(texto));

        acao.Should().Throw<DominioException>()
            .Which.Codigo.Should().Be("lancamento.valor_nao_positivo");
    }

    [Theory]
    [InlineData("10.123")]
    [InlineData("0.001")]
    public void PrecisaoAcimaDeDuasCasasEhRejeitada(string texto)
    {
        var acao = () => Dinheiro.EmReais(Valor(texto));

        acao.Should().Throw<DominioException>()
            .Which.Codigo.Should().Be("lancamento.precisao_invalida");
    }

    // 10.5000m carrega escala 4, mas só duas casas significativas.
    [Fact]
    public void ZerosNaoSignificativosNaoContamComoCasaDecimal() =>
        Dinheiro.EmReais(10.5000m).Valor.Should().Be(10.5000m);

    [Fact]
    public void MesmoValorNaMesmaMoedaSaoIguais() =>
        Dinheiro.EmReais(10m).Should().Be(Dinheiro.EmReais(10m));

    [Fact]
    public void MesmoValorEmMoedasDiferentesNaoSaoIguais() =>
        Dinheiro.Criar(10m, Moeda.Criar("USD")).Should().NotBe(Dinheiro.EmReais(10m));
}
