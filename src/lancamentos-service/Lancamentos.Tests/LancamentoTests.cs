using FluentAssertions;
using Lancamentos.Domain;

namespace Lancamentos.Tests;

public class LancamentoTests
{
    // 01h UTC do dia 27 é ainda o dia 26 em São Paulo: as duas datas divergem
    // de propósito, para que o teste falhe se a regra passar a olhar o UTC.
    private static readonly DateOnly HojeEmSaoPaulo = new(2026, 7, 26);
    private static readonly DateTimeOffset AgoraUtc = new(2026, 7, 27, 1, 0, 0, TimeSpan.Zero);
    private static readonly IRelogio Relogio = new RelogioFalso(AgoraUtc, HojeEmSaoPaulo);

    private const string HashValido = "0000000000000000000000000000000000000000000000000000000000000000";

    private static Lancamento Criar(
        TipoLancamento tipo = TipoLancamento.Credito,
        DateOnly? dataCompetencia = null,
        string? descricao = null,
        string chave = "chave-1",
        Guid? comercianteId = null) =>
        Lancamento.Criar(
            comercianteId ?? Guid.NewGuid(),
            tipo,
            Dinheiro.EmReais(100m),
            dataCompetencia ?? HojeEmSaoPaulo,
            descricao,
            chave,
            HashValido,
            Relogio);

    [Fact]
    public void LancamentoValidoEhCriado()
    {
        var lancamento = Criar(TipoLancamento.Debito, descricao: "Compra de insumos");

        lancamento.Id.Should().NotBe(Guid.Empty);
        lancamento.Tipo.Should().Be(TipoLancamento.Debito);
        lancamento.Valor.Valor.Should().Be(100m);
        lancamento.CriadoEm.Should().Be(AgoraUtc);
        lancamento.EhEstorno.Should().BeFalse();
    }

    [Fact]
    public void DataDeHojeNoFusoDoComercianteEhAceita() =>
        Criar(dataCompetencia: HojeEmSaoPaulo).DataCompetencia.Should().Be(HojeEmSaoPaulo);

    // O dia 27 já começou em UTC, mas não no fuso do comerciante.
    [Fact]
    public void DataFuturaNoFusoDoComercianteEhRejeitadaAindaQueSejaHojeEmUtc()
    {
        var acao = () => Criar(dataCompetencia: HojeEmSaoPaulo.AddDays(1));

        acao.Should().Throw<DominioException>()
            .Which.Codigo.Should().Be("lancamento.data_competencia_futura");
    }

    [Fact]
    public void DataRetroativaEhAceita() =>
        Criar(dataCompetencia: HojeEmSaoPaulo.AddDays(-30)).DataCompetencia
            .Should().Be(HojeEmSaoPaulo.AddDays(-30));

    [Fact]
    public void ComercianteVazioEhRejeitado()
    {
        var acao = () => Criar(comercianteId: Guid.Empty);

        acao.Should().Throw<DominioException>()
            .Which.Codigo.Should().Be("lancamento.campo_obrigatorio");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ChaveIdempotenciaVaziaEhRejeitada(string chave)
    {
        var acao = () => Criar(chave: chave);

        acao.Should().Throw<DominioException>()
            .Which.Codigo.Should().Be("lancamento.campo_obrigatorio");
    }

    [Fact]
    public void ChaveIdempotenciaAcimaDoLimiteEhRejeitada()
    {
        var acao = () => Criar(chave: new string('k', Lancamento.TamanhoMaximoChaveIdempotencia + 1));

        acao.Should().Throw<DominioException>()
            .Which.Codigo.Should().Be("lancamento.campo_acima_do_limite");
    }

    [Fact]
    public void DescricaoAcimaDoLimiteEhRejeitada()
    {
        var acao = () => Criar(descricao: new string('d', Lancamento.TamanhoMaximoDescricao + 1));

        acao.Should().Throw<DominioException>()
            .Which.Codigo.Should().Be("lancamento.campo_acima_do_limite");
    }

    [Fact]
    public void DescricaoEmBrancoViraNula() => Criar(descricao: "   ").Descricao.Should().BeNull();

    [Fact]
    public void EstornoInverteOTipoEPreservaValorEData()
    {
        var original = Criar(TipoLancamento.Credito);

        var estorno = original.Estornar("chave-estorno", HashValido, Relogio);

        estorno.Tipo.Should().Be(TipoLancamento.Debito);
        estorno.Valor.Should().Be(original.Valor);
        estorno.DataCompetencia.Should().Be(original.DataCompetencia);
        estorno.ComercianteId.Should().Be(original.ComercianteId);
        estorno.EstornoDeId.Should().Be(original.Id);
        estorno.EhEstorno.Should().BeTrue();
    }

    [Fact]
    public void EstornoDeEstornoEhRejeitado()
    {
        var estorno = Criar().Estornar("chave-estorno", HashValido, Relogio);

        var acao = () => estorno.Estornar("chave-estorno-2", HashValido, Relogio);

        acao.Should().Throw<DominioException>()
            .Which.Codigo.Should().Be("lancamento.estorno_de_estorno");
    }
}
