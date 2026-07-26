using Consolidado.Domain;
using Dapper;
using Npgsql;

namespace Consolidado.Infrastructure.Persistencia;

public sealed class SaldoDiarioRepository(NpgsqlDataSource dataSource) : ISaldoDiarioRepository
{
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

        const string sqlUpsert = """
            INSERT INTO saldo_diario (comerciante_id, data, moeda,
                                      total_debitos, total_creditos, saldo,
                                      qtd_lancamentos, atualizado_em)
            VALUES (@comercianteId, @data, @moeda, @debito, @credito, @saldo, 1, now())
            ON CONFLICT (comerciante_id, data, moeda) DO UPDATE SET
                total_debitos   = saldo_diario.total_debitos  + EXCLUDED.total_debitos,
                total_creditos  = saldo_diario.total_creditos + EXCLUDED.total_creditos,
                saldo           = saldo_diario.saldo + EXCLUDED.saldo,
                qtd_lancamentos = saldo_diario.qtd_lancamentos + 1,
                atualizado_em   = now();
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

        await conexao.ExecuteAsync(new CommandDefinition(sqlUpsert, new
        {
            comercianteId = evento.ComercianteId,
            data = evento.DataCompetencia,
            moeda = evento.Moeda,
            debito = movimento.Debito,
            credito = movimento.Credito,
            saldo = movimento.Saldo,
        }, transacao, cancellationToken: cancellationToken));

        await transacao.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<SaldoDiario?> ObterAsync(
        Guid comercianteId,
        DateOnly data,
        string moeda,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT comerciante_id, data, moeda, total_debitos, total_creditos,
                   saldo, qtd_lancamentos, atualizado_em
            FROM saldo_diario
            WHERE comerciante_id = @comercianteId AND data = @data AND moeda = @moeda;
            """;

        await using var conexao = await dataSource.OpenConnectionAsync(cancellationToken);

        return await conexao.QueryFirstOrDefaultAsync<SaldoDiario>(new CommandDefinition(
            sql, new { comercianteId, data, moeda }, cancellationToken: cancellationToken));
    }
}
