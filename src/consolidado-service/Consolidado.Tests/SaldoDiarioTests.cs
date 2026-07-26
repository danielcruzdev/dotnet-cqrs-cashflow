using Consolidado.Domain;
using FluentAssertions;

namespace Consolidado.Tests;

public class SaldoDiarioTests
{
    [Fact]
    public void CreditoSomaAoSaldo() =>
        Movimento.De("CREDITO", 150m).Should().Be(new Movimento(0m, 150m, 150m));

    [Fact]
    public void DebitoSubtraiDoSaldo() =>
        Movimento.De("DEBITO", 150m).Should().Be(new Movimento(150m, 0m, -150m));

    [Fact]
    public void TipoDesconhecidoEhRejeitado()
    {
        var acao = () => Movimento.De("TRANSFERENCIA", 10m);

        acao.Should().Throw<ArgumentOutOfRangeException>();
    }

    // Estorno é lançamento contrário: zera o saldo sem apagar a movimentação bruta.
    [Fact]
    public void EstornoZeraOSaldoMasPreservaAMovimentacaoBruta()
    {
        var credito = Movimento.De("CREDITO", 100m);
        var estorno = Movimento.De("DEBITO", 100m);

        (credito.Saldo + estorno.Saldo).Should().Be(0m);
        (credito.Credito + estorno.Credito).Should().Be(100m);
        (credito.Debito + estorno.Debito).Should().Be(100m);
    }

    [Fact]
    public void SequenciaDeLancamentosProduzOSaldoLiquido()
    {
        decimal[] valores = [200m, 50m];

        var saldo = Movimento.De("CREDITO", valores[0]).Saldo + Movimento.De("DEBITO", valores[1]).Saldo;

        saldo.Should().Be(150m);
    }

    // Dia sem lançamento é zero, não ausência de dado.
    [Fact]
    public void DiaSemLancamentosDevolveZeros()
    {
        var comerciante = Guid.NewGuid();
        var data = new DateOnly(2026, 7, 26);

        var saldo = SaldoDiario.Vazio(comerciante, data, "BRL");

        saldo.ComercianteId.Should().Be(comerciante);
        saldo.Data.Should().Be(data);
        saldo.Moeda.Should().Be("BRL");
        saldo.Saldo.Should().Be(0m);
        saldo.TotalCreditos.Should().Be(0m);
        saldo.TotalDebitos.Should().Be(0m);
        saldo.QtdLancamentos.Should().Be(0);
        saldo.AtualizadoEm.Should().BeNull();
    }
}
