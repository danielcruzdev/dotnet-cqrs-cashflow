using System.Net;
using System.Text;
using Consolidado.Domain;
using Dapper;
using FluentAssertions;
using Npgsql;
using RabbitMQ.Client;

namespace Cashflow.E2E.Tests;

/// <summary>
/// 11.5 — as duas idempotências do sistema: a da escrita, pela Idempotency-Key,
/// e a do consumo, pelo eventId. Sem as duas, um retry vira dinheiro a mais.
/// </summary>
[Collection(nameof(AmbienteCashflow))]
public class IdempotenciaTests(AmbienteCashflow ambiente)
{
    private static readonly DateOnly Competencia = AmbienteCashflow.HojeDoComerciante;

    // Margem para uma eventual segunda aplicação aparecer antes da asserção final.
    private static readonly TimeSpan Assentamento = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task MesmaChaveComMesmoPayloadNaoDuplica()
    {
        var comerciante = Guid.NewGuid();
        var chave = Guid.NewGuid().ToString();
        using var lancamentos = await ambiente.ClienteLancamentosAsync(comerciante);

        using var primeira = await lancamentos.CriarLancamentoAsync(
            comerciante, "CREDITO", 100m, Competencia, chave);

        using var repeticao = await lancamentos.CriarLancamentoAsync(
            comerciante, "CREDITO", 100m, Competencia, chave);

        primeira.StatusCode.Should().Be(HttpStatusCode.Created);

        // 200, não 201: nada foi criado nesta requisição.
        repeticao.StatusCode.Should().Be(HttpStatusCode.OK);
        (await repeticao.IdDoLancamentoAsync()).Should().Be(await primeira.IdDoLancamentoAsync());

        var saldo = await AguardarConvergenciaAsync(comerciante, 1);

        saldo.QtdLancamentos.Should().Be(1);
        saldo.Saldo.Should().Be(100m);
    }

    // Mesma chave com conteúdo diferente não é retry: é reuso indevido.
    [Fact]
    public async Task MesmaChaveComPayloadDiferenteEhConflito()
    {
        var comerciante = Guid.NewGuid();
        var chave = Guid.NewGuid().ToString();
        using var lancamentos = await ambiente.ClienteLancamentosAsync(comerciante);

        using var primeira = await lancamentos.CriarLancamentoAsync(
            comerciante, "CREDITO", 100m, Competencia, chave);

        using var divergente = await lancamentos.CriarLancamentoAsync(
            comerciante, "CREDITO", 250m, Competencia, chave);

        primeira.StatusCode.Should().Be(HttpStatusCode.Created);
        divergente.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // O broker entrega at-least-once: a mesma mensagem pode chegar duas vezes.
    [Fact]
    public async Task EventoReentregueNaoSomaDuasVezes()
    {
        var comerciante = Guid.NewGuid();
        using var lancamentos = await ambiente.ClienteLancamentosAsync(comerciante);

        using var criacao = await lancamentos.CriarLancamentoAsync(
            comerciante, "DEBITO", 40m, Competencia, Guid.NewGuid().ToString());

        criacao.StatusCode.Should().Be(HttpStatusCode.Created);

        var lancamentoId = await criacao.IdDoLancamentoAsync();
        await AguardarConvergenciaAsync(comerciante, 1);

        await RepublicarAsync(await LerDaOutboxAsync(lancamentoId));
        await Task.Delay(Assentamento);

        using var consolidado = await ambiente.ClienteConsolidadoAsync(comerciante);
        var saldo = await consolidado.ObterSaldoAsync(comerciante, Competencia);

        saldo.QtdLancamentos.Should().Be(1);
        saldo.TotalDebitos.Should().Be(40m);
        saldo.Saldo.Should().Be(-40m);
    }

    private async Task<SaldoDiario> AguardarConvergenciaAsync(Guid comerciante, int quantidade)
    {
        using var consolidado = await ambiente.ClienteConsolidadoAsync(comerciante);

        await AmbienteCashflow.AguardarAsync(
            () => consolidado.ObterSaldoAsync(comerciante, Competencia),
            atual => atual.QtdLancamentos >= quantidade);

        await Task.Delay(Assentamento);

        return await consolidado.ObterSaldoAsync(comerciante, Competencia);
    }

    /// <summary>O payload exato que o publisher mandou para o broker.</summary>
    private async Task<string> LerDaOutboxAsync(Guid lancamentoId)
    {
        await using var conexao = new NpgsqlConnection(ambiente.ConexaoLancamentos);

        return await conexao.QuerySingleAsync<string>(
            "SELECT CAST(payload AS text) FROM outbox_messages WHERE agregado_id = @lancamentoId",
            new { lancamentoId });
    }

    private async Task RepublicarAsync(string payload)
    {
        await using var conexao = await ambiente.FabricaDoBroker.CreateConnectionAsync();
        await using var canal = await conexao.CreateChannelAsync();

        await canal.BasicPublishAsync(
            exchange: "lancamentos.events",
            routingKey: "lancamento.realizado.v1",
            mandatory: false,
            basicProperties: new BasicProperties(),
            body: Encoding.UTF8.GetBytes(payload));
    }
}
