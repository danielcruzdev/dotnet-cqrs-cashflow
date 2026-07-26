using System.Text.Json;
using Consolidado.Domain;
using Dapper;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Npgsql;
using Polly;

namespace Consolidado.Infrastructure.Persistencia;

public sealed partial class SaldoDiarioRepository(
    NpgsqlDataSource dataSource,
    IDistributedCache cache,
    ResiliencePipeline resiliencia,
    ILogger<SaldoDiarioRepository> logger) : ISaldoDiarioRepository
{
    private static readonly TimeZoneInfo FusoComerciante =
        TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string ColunasSelect = """
        comerciante_id, data, moeda, total_debitos, total_creditos,
        saldo, qtd_lancamentos, atualizado_em
        """;

    public async Task<bool> AplicarAsync(
        LancamentoRealizado evento,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evento);

        const string sqlDedupe = """
            INSERT INTO eventos_processados (event_id)
            VALUES (@eventId)
            ON CONFLICT DO NOTHING;
            """;

        var sqlUpsert = $"""
            INSERT INTO saldo_diario (comerciante_id, data, moeda,
                                      total_debitos, total_creditos, saldo,
                                      qtd_lancamentos, atualizado_em)
            VALUES (@comercianteId, @data, @moeda, @debito, @credito, @saldo, 1, now())
            ON CONFLICT (comerciante_id, data, moeda) DO UPDATE SET
                total_debitos   = saldo_diario.total_debitos  + EXCLUDED.total_debitos,
                total_creditos  = saldo_diario.total_creditos + EXCLUDED.total_creditos,
                saldo           = saldo_diario.saldo + EXCLUDED.saldo,
                qtd_lancamentos = saldo_diario.qtd_lancamentos + 1,
                atualizado_em   = now()
            RETURNING {ColunasSelect};
            """;

        await using var conexao = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transacao = await conexao.BeginTransactionAsync(cancellationToken);

        // ON CONFLICT DO NOTHING em vez de capturar a violação: no PostgreSQL, PK
        // violada aborta a transação inteira e nem o COMMIT passaria.
        var inseridos = await conexao.ExecuteAsync(new CommandDefinition(
            sqlDedupe, new { eventId = evento.EventId }, transacao,
            cancellationToken: cancellationToken));

        if (inseridos == 0)
        {
            await transacao.RollbackAsync(cancellationToken);
            return false;
        }

        var movimento = Movimento.De(evento.Tipo, evento.Valor);

        var atualizado = await conexao.QuerySingleAsync<SaldoDiario>(new CommandDefinition(sqlUpsert, new
        {
            comercianteId = evento.ComercianteId,
            data = evento.DataCompetencia,
            moeda = evento.Moeda,
            debito = movimento.Debito,
            credito = movimento.Credito,
            saldo = movimento.Saldo,
        }, transacao, cancellationToken: cancellationToken));

        await transacao.CommitAsync(cancellationToken);

        // SET com o valor novo, não DEL: com DEL, um leitor que buscou antes da
        // escrita pode repopular o cache com o valor antigo depois.
        await GravarNoCacheAsync(atualizado, cancellationToken);

        return true;
    }

    public async Task<SaldoDiario?> ObterAsync(
        Guid comercianteId,
        DateOnly data,
        string moeda,
        CancellationToken cancellationToken = default)
    {
        var doCache = await LerDoCacheAsync(Chave(comercianteId, data, moeda), cancellationToken);

        if (doCache is not null)
        {
            return doCache;
        }

        var sql = $"""
            SELECT {ColunasSelect}
            FROM saldo_diario
            WHERE comerciante_id = @comercianteId AND data = @data AND moeda = @moeda;
            """;

        var saldo = await resiliencia.ExecuteAsync(async ct =>
        {
            await using var conexao = await dataSource.OpenConnectionAsync(ct);

            return await conexao.QueryFirstOrDefaultAsync<SaldoDiario>(new CommandDefinition(
                sql, new { comercianteId, data, moeda }, cancellationToken: ct));
        }, cancellationToken);

        if (saldo is not null)
        {
            await GravarNoCacheAsync(saldo, cancellationToken);
        }

        return saldo;
    }

    public async Task<IReadOnlyList<SaldoDiario>> ListarPeriodoAsync(
        Guid comercianteId,
        DateOnly de,
        DateOnly ate,
        string moeda,
        CancellationToken cancellationToken = default)
    {
        // Sem cache: a invalidação do consumer é por dia e não alcançaria uma
        // resposta de período inteira, que ficaria servindo dado velho.
        var sql = $"""
            SELECT {ColunasSelect}
            FROM saldo_diario
            WHERE comerciante_id = @comercianteId
              AND moeda = @moeda
              AND data BETWEEN @de AND @ate
            ORDER BY data;
            """;

        var linhas = await resiliencia.ExecuteAsync(async ct =>
        {
            await using var conexao = await dataSource.OpenConnectionAsync(ct);

            return await conexao.QueryAsync<SaldoDiario>(new CommandDefinition(
                sql, new { comercianteId, de, ate, moeda }, cancellationToken: ct));
        }, cancellationToken);

        return [.. linhas];
    }

    private async Task<SaldoDiario?> LerDoCacheAsync(string chave, CancellationToken cancellationToken)
    {
        try
        {
            var json = await cache.GetStringAsync(chave, cancellationToken);

            return json is null ? null : JsonSerializer.Deserialize<SaldoDiario>(json, Json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogCacheIndisponivel(logger, ex);
            return null;
        }
    }

    private async Task GravarNoCacheAsync(SaldoDiario saldo, CancellationToken cancellationToken)
    {
        // Dia passado muda pouco, mas não é imutável: lançamento retroativo e
        // estorno de dia antigo são permitidos.
        var hoje = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, FusoComerciante).DateTime);

        var ttl = saldo.Data >= hoje ? TimeSpan.FromSeconds(5) : TimeSpan.FromMinutes(5);

        try
        {
            await cache.SetStringAsync(
                Chave(saldo.ComercianteId, saldo.Data, saldo.Moeda),
                JsonSerializer.Serialize(saldo, Json),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogCacheIndisponivel(logger, ex);
        }
    }

    private static string Chave(Guid comercianteId, DateOnly data, string moeda) =>
        $"consolidado:{comercianteId}:{moeda}:{data:yyyy-MM-dd}";

    [LoggerMessage(EventId = 3100, Level = LogLevel.Warning,
        Message = "Cache indisponível; seguindo direto para o banco")]
    private static partial void LogCacheIndisponivel(ILogger logger, Exception excecao);
}
