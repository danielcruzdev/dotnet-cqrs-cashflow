using System.Globalization;
using System.Net.Http.Json;
using Consolidado.Domain;

namespace Cashflow.E2E.Tests;

/// <summary>Chamadas HTTP dos dois serviços, do jeito que um cliente real faria.</summary>
internal static class Api
{
    public static Task<HttpResponseMessage> CriarLancamentoAsync(
        this HttpClient cliente,
        Guid comercianteId,
        string tipo,
        decimal valor,
        DateOnly dataCompetencia,
        string chaveIdempotencia,
        string? descricao = null)
    {
        var requisicao = new HttpRequestMessage(HttpMethod.Post, "/api/lancamentos")
        {
            Content = JsonContent.Create(new
            {
                comercianteId,
                tipo,
                valor,
                moeda = "BRL",
                dataCompetencia,
                descricao,
            }),
        };

        requisicao.Headers.Add("Idempotency-Key", chaveIdempotencia);

        return cliente.SendAsync(requisicao);
    }

    public static Task<HttpResponseMessage> EstornarAsync(
        this HttpClient cliente,
        Guid comercianteId,
        Guid lancamentoId,
        string chaveIdempotencia)
    {
        var requisicao = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/lancamentos/{lancamentoId}/estorno?comercianteId={comercianteId}");

        requisicao.Headers.Add("Idempotency-Key", chaveIdempotencia);

        return cliente.SendAsync(requisicao);
    }

    public static async Task<SaldoDiario> ObterSaldoAsync(
        this HttpClient cliente,
        Guid comercianteId,
        DateOnly data)
    {
        var caminho = $"/api/consolidado/{comercianteId}/{data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

        using var resposta = await cliente.GetAsync(caminho);
        resposta.EnsureSuccessStatusCode();

        return (await resposta.Content.ReadFromJsonAsync<SaldoDiario>())!;
    }

    public static async Task<Guid> IdDoLancamentoAsync(this HttpResponseMessage resposta)
    {
        var corpo = await resposta.Content.ReadFromJsonAsync<LancamentoCriado>();

        return corpo!.Id;
    }

    private sealed record LancamentoCriado(Guid Id);
}
