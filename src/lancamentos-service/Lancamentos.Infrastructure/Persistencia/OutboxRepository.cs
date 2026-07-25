using System.Text.Json;
using Dapper;
using Lancamentos.Domain.Abstracoes;
using Lancamentos.Domain.Eventos;

namespace Lancamentos.Infrastructure.Persistencia;

/// <summary>Mensagem pendente lida da outbox pelo publisher.</summary>
public sealed record MensagemOutbox
{
    public Guid Id { get; init; }
    public Guid EventId { get; init; }
    public string TipoEvento { get; init; } = string.Empty;
    public string Payload { get; init; } = string.Empty;
    public string RoutingKey { get; init; } = string.Empty;
    public int Tentativas { get; init; }
}

/// <inheritdoc cref="IOutboxWriter"/>
public sealed class OutboxRepository(SessaoDeBanco sessao) : IOutboxWriter
{
    /// <summary>
    /// Acima disso a linha é considerada envenenada e sai do lote normal.
    /// </summary>
    /// <remarks>
    /// Sem esse teto, uma única mensagem impublicável (payload corrompido, tipo
    /// desconhecido) seria retentada para sempre e travaria a ordem de tudo que
    /// vem depois dela. É o equivalente da DLQ, do lado do produtor.
    /// </remarks>
    public const int MaximoTentativas = 10;

    private static readonly JsonSerializerOptions OpcoesJson = new(JsonSerializerDefaults.Web);

    public async Task EscreverAsync(EventoDeDominio evento, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evento);

        const string sql = """
            INSERT INTO outbox_messages (
                event_id, agregado_id, comerciante_id, data_competencia,
                tipo_evento, payload, criado_em)
            VALUES (
                @EventId, @AgregadoId, @ComercianteId, @DataCompetencia,
                @TipoEvento, CAST(@Payload AS jsonb), @CriadoEm);
            """;

        var conexao = await sessao.ObterConexaoAsync(cancellationToken);

        await conexao.ExecuteAsync(new CommandDefinition(sql, new
        {
            evento.EventId,
            evento.AgregadoId,
            evento.ComercianteId,
            evento.DataCompetencia,
            TipoEvento = evento.EventType,
            Payload = JsonSerializer.Serialize(evento, evento.GetType(), OpcoesJson),
            CriadoEm = evento.OccurredAt,
        }, sessao.Transacao, cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Reserva um lote de mensagens pendentes para publicação.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>FOR UPDATE SKIP LOCKED</c> é o que permite rodar várias réplicas do
    /// publisher sem publicar duplicado: cada uma trava um lote disjunto e as
    /// demais simplesmente pulam as linhas já travadas, em vez de ficarem
    /// bloqueadas esperando.
    /// </para>
    /// <para>
    /// Precisa ser chamado <b>dentro de uma transação</b> — a trava de linha só
    /// existe enquanto ela estiver aberta.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<MensagemOutbox>> ReservarPendentesAsync(
        int tamanhoLote,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, event_id, tipo_evento, CAST(payload AS text) AS payload, tentativas
            FROM outbox_messages
            WHERE processado_em IS NULL
              AND tentativas < @maximoTentativas
            ORDER BY criado_em
            LIMIT @tamanhoLote
            FOR UPDATE SKIP LOCKED;
            """;

        var conexao = await sessao.ObterConexaoAsync(cancellationToken);

        var linhas = await conexao.QueryAsync<MensagemOutbox>(new CommandDefinition(
            sql,
            new { tamanhoLote, maximoTentativas = MaximoTentativas },
            sessao.Transacao,
            cancellationToken: cancellationToken));

        return [.. linhas];
    }

    public async Task MarcarProcessadaAsync(Guid id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE outbox_messages
            SET processado_em = now()
            WHERE id = @id;
            """;

        var conexao = await sessao.ObterConexaoAsync(cancellationToken);

        await conexao.ExecuteAsync(new CommandDefinition(
            sql, new { id }, sessao.Transacao, cancellationToken: cancellationToken));
    }

    public async Task RegistrarFalhaAsync(Guid id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE outbox_messages
            SET tentativas = tentativas + 1
            WHERE id = @id;
            """;

        var conexao = await sessao.ObterConexaoAsync(cancellationToken);

        await conexao.ExecuteAsync(new CommandDefinition(
            sql, new { id }, sessao.Transacao, cancellationToken: cancellationToken));
    }

    /// <summary>Mensagens que esgotaram as tentativas e precisam de intervenção.</summary>
    public async Task<int> ContarEnvenenadasAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT count(*)
            FROM outbox_messages
            WHERE processado_em IS NULL AND tentativas >= @maximoTentativas;
            """;

        var conexao = await sessao.ObterConexaoAsync(cancellationToken);

        return await conexao.ExecuteScalarAsync<int>(new CommandDefinition(
            sql, new { maximoTentativas = MaximoTentativas }, sessao.Transacao,
            cancellationToken: cancellationToken));
    }
}
