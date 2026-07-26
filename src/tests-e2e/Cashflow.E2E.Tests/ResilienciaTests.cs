using System.Net;
using Dapper;
using FluentAssertions;
using Npgsql;

namespace Cashflow.E2E.Tests;

/// <summary>
/// Fase 12 — o requisito âncora (RNF-01): o registro de lançamentos não pode
/// ficar indisponível quando o consolidado cai. É o único teste que prova o
/// requisito principal em vez de argumentar sobre ele.
/// </summary>
[Collection(nameof(AmbienteCashflow))]
public class ResilienciaTests(AmbienteCashflow ambiente)
{
    private const int Quantidade = 5;
    private const decimal Valor = 10m;

    private static readonly DateOnly Competencia = AmbienteCashflow.HojeDoComerciante;
    private static readonly TimeSpan PrazoDeVolta = TimeSpan.FromSeconds(90);

    [Fact]
    public async Task ConsolidadoForaDoArNaoImpedeLancamentoEOSaldoConvergeNaVolta()
    {
        var comerciante = Guid.NewGuid();
        using var lancamentos = await ambiente.ClienteLancamentosAsync(comerciante);

        await ambiente.DerrubarConsolidadoAsync();

        try
        {
            await RegistrarAsync(lancamentos, comerciante, "CREDITO");

            // Sem consumer a projeção não pode ter avançado — é isso que garante
            // que a convergência abaixo é efeito da volta, e não de uma corrida.
            var projetadas = await ContarAsync(
                ambiente.ConexaoConsolidado,
                "SELECT COUNT(*) FROM saldo_diario WHERE comerciante_id = @comerciante",
                comerciante);

            projetadas.Should().Be(0);
        }
        finally
        {
            ambiente.SubirConsolidado();
        }

        using var consolidado = await ambiente.ClienteConsolidadoAsync(comerciante);

        var saldo = await AmbienteCashflow.AguardarAsync(
            () => consolidado.ObterSaldoAsync(comerciante, Competencia),
            atual => atual.QtdLancamentos == Quantidade,
            PrazoDeVolta);

        saldo.QtdLancamentos.Should().Be(Quantidade);
        saldo.TotalCreditos.Should().Be(Quantidade * Valor);
        saldo.Saldo.Should().Be(Quantidade * Valor);
    }

    [Fact]
    public async Task BrokerForaDoArNaoImpedeLancamentoENenhumEventoSePerde()
    {
        var comerciante = Guid.NewGuid();
        using var lancamentos = await ambiente.ClienteLancamentosAsync(comerciante);

        await ambiente.DerrubarBrokerAsync();

        try
        {
            await RegistrarAsync(lancamentos, comerciante, "DEBITO");

            // É o Outbox: o evento foi gravado na transação do lançamento e fica
            // retido até o broker voltar. Publicar direto perderia os cinco.
            (await PendentesNaOutboxAsync(comerciante)).Should().Be(Quantidade);
        }
        finally
        {
            await ambiente.SubirBrokerAsync();
        }

        var pendentes = await AmbienteCashflow.AguardarAsync(
            () => PendentesNaOutboxAsync(comerciante),
            restantes => restantes == 0,
            PrazoDeVolta);

        pendentes.Should().Be(0);

        using var consolidado = await ambiente.ClienteConsolidadoAsync(comerciante);

        var saldo = await AmbienteCashflow.AguardarAsync(
            () => consolidado.ObterSaldoAsync(comerciante, Competencia),
            atual => atual.QtdLancamentos == Quantidade,
            PrazoDeVolta);

        saldo.QtdLancamentos.Should().Be(Quantidade);
        saldo.TotalDebitos.Should().Be(Quantidade * Valor);
        saldo.Saldo.Should().Be(-(Quantidade * Valor));
    }

    private static async Task RegistrarAsync(HttpClient cliente, Guid comerciante, string tipo)
    {
        for (var i = 0; i < Quantidade; i++)
        {
            using var resposta = await cliente.CriarLancamentoAsync(
                comerciante, tipo, Valor, Competencia, Guid.NewGuid().ToString());

            resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        }
    }

    private Task<long> PendentesNaOutboxAsync(Guid comerciante) => ContarAsync(
        ambiente.ConexaoLancamentos,
        "SELECT COUNT(*) FROM outbox_messages WHERE comerciante_id = @comerciante AND processado_em IS NULL",
        comerciante);

    private static async Task<long> ContarAsync(string conexao, string sql, Guid comerciante)
    {
        await using var conectada = new NpgsqlConnection(conexao);

        return await conectada.ExecuteScalarAsync<long>(sql, new { comerciante });
    }
}
