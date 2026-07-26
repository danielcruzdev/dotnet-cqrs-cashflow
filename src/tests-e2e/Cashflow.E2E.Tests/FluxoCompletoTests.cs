using System.Net;
using FluentAssertions;

namespace Cashflow.E2E.Tests;

/// <summary>
/// 11.4 — o caminho inteiro: POST no write model, outbox, broker, consumer e
/// leitura no read model. É o teste que prova que os dois serviços conversam.
/// </summary>
[Collection(nameof(AmbienteCashflow))]
public class FluxoCompletoTests(AmbienteCashflow ambiente)
{
    private static readonly DateOnly Competencia = AmbienteCashflow.HojeDoComerciante;

    [Fact]
    public async Task LancamentoRegistradoChegaAoSaldoDiario()
    {
        var comerciante = Guid.NewGuid();
        using var lancamentos = await ambiente.ClienteLancamentosAsync(comerciante);

        using var criacao = await lancamentos.CriarLancamentoAsync(
            comerciante, "CREDITO", 150.75m, Competencia, Guid.NewGuid().ToString());

        criacao.StatusCode.Should().Be(HttpStatusCode.Created);

        using var consolidado = await ambiente.ClienteConsolidadoAsync(comerciante);

        var saldo = await AmbienteCashflow.AguardarAsync(
            () => consolidado.ObterSaldoAsync(comerciante, Competencia),
            atual => atual.QtdLancamentos == 1);

        saldo.QtdLancamentos.Should().Be(1);
        saldo.TotalCreditos.Should().Be(150.75m);
        saldo.TotalDebitos.Should().Be(0m);
        saldo.Saldo.Should().Be(150.75m);
        saldo.Moeda.Should().Be("BRL");
    }

    [Fact]
    public async Task DiaSemLancamentosRespondeZerosENao404()
    {
        var comerciante = Guid.NewGuid();
        using var consolidado = await ambiente.ClienteConsolidadoAsync(comerciante);

        var saldo = await consolidado.ObterSaldoAsync(comerciante, Competencia);

        saldo.QtdLancamentos.Should().Be(0);
        saldo.Saldo.Should().Be(0m);
    }

    // Estorno é lançamento contrário: o saldo zera, a movimentação bruta não.
    [Fact]
    public async Task EstornoZeraOSaldoSemApagarAMovimentacao()
    {
        var comerciante = Guid.NewGuid();
        using var lancamentos = await ambiente.ClienteLancamentosAsync(comerciante);

        using var criacao = await lancamentos.CriarLancamentoAsync(
            comerciante, "CREDITO", 80m, Competencia, Guid.NewGuid().ToString());

        criacao.StatusCode.Should().Be(HttpStatusCode.Created);
        var lancamentoId = await criacao.IdDoLancamentoAsync();

        using var estorno = await lancamentos.EstornarAsync(
            comerciante, lancamentoId, Guid.NewGuid().ToString());

        estorno.StatusCode.Should().Be(HttpStatusCode.Created);

        using var consolidado = await ambiente.ClienteConsolidadoAsync(comerciante);

        var saldo = await AmbienteCashflow.AguardarAsync(
            () => consolidado.ObterSaldoAsync(comerciante, Competencia),
            atual => atual.QtdLancamentos == 2);

        saldo.QtdLancamentos.Should().Be(2);
        saldo.TotalCreditos.Should().Be(80m);
        saldo.TotalDebitos.Should().Be(80m);
        saldo.Saldo.Should().Be(0m);
    }
}
