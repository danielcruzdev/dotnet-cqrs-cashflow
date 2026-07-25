using Dapper;
using Lancamentos.Domain;
using Lancamentos.Domain.Abstracoes;
using Npgsql;

namespace Lancamentos.Infrastructure.Persistencia;

/// <inheritdoc cref="ILancamentoRepository"/>
public sealed class LancamentoRepository(SessaoDeBanco sessao) : ILancamentoRepository
{
    /// <summary>SqlState do PostgreSQL para violação de constraint única.</summary>
    private const string UniqueViolation = "23505";

    private const string ColunasSelect = """
        id, comerciante_id, tipo, valor, moeda, data_competencia,
        descricao, estorno_de_id, chave_idempotencia, hash_payload, criado_em
        """;

    public async Task AdicionarAsync(Lancamento lancamento, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lancamento);

        const string sql = """
            INSERT INTO lancamentos (
                id, comerciante_id, tipo, valor, moeda, data_competencia,
                descricao, estorno_de_id, chave_idempotencia, hash_payload, criado_em)
            VALUES (
                @Id, @ComercianteId, @Tipo, @Valor, @Moeda, @DataCompetencia,
                @Descricao, @EstornoDeId, @ChaveIdempotencia, @HashPayload, @CriadoEm);
            """;

        var conexao = await sessao.ObterConexaoAsync(cancellationToken);

        try
        {
            await conexao.ExecuteAsync(new CommandDefinition(sql, new
            {
                lancamento.Id,
                lancamento.ComercianteId,
                Tipo = lancamento.Tipo.ParaPersistencia(),
                Valor = lancamento.Valor.Valor,
                Moeda = lancamento.Valor.Moeda.Codigo,
                lancamento.DataCompetencia,
                lancamento.Descricao,
                lancamento.EstornoDeId,
                lancamento.ChaveIdempotencia,
                lancamento.HashPayload,
                lancamento.CriadoEm,
            }, sessao.Transacao, cancellationToken: cancellationToken));
        }
        catch (PostgresException ex) when (ex.SqlState == UniqueViolation
                                           && ex.ConstraintName == "uq_lancamento_idempotencia")
        {
            throw new ChaveIdempotenciaEmUsoException(
                lancamento.ComercianteId, lancamento.ChaveIdempotencia, ex);
        }
    }

    public async Task<Lancamento?> ObterPorIdAsync(
        Guid comercianteId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        // Filtra por dono na consulta, não só na autorização.
        var sql = $"""
            SELECT {ColunasSelect}
            FROM lancamentos
            WHERE comerciante_id = @comercianteId AND id = @id;
            """;

        return await ConsultarUmAsync(sql, new { comercianteId, id }, cancellationToken);
    }

    public async Task<Lancamento?> ObterPorChaveIdempotenciaAsync(
        Guid comercianteId,
        string chaveIdempotencia,
        CancellationToken cancellationToken = default)
    {
        var sql = $"""
            SELECT {ColunasSelect}
            FROM lancamentos
            WHERE comerciante_id = @comercianteId AND chave_idempotencia = @chaveIdempotencia;
            """;

        return await ConsultarUmAsync(sql, new { comercianteId, chaveIdempotencia }, cancellationToken);
    }

    public async Task<bool> PossuiEstornoAsync(Guid lancamentoId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT EXISTS (SELECT 1 FROM lancamentos WHERE estorno_de_id = @lancamentoId);
            """;

        var conexao = await sessao.ObterConexaoAsync(cancellationToken);

        return await conexao.ExecuteScalarAsync<bool>(new CommandDefinition(
            sql, new { lancamentoId }, sessao.Transacao, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<Lancamento>> ListarPorPeriodoAsync(
        Guid comercianteId,
        DateOnly inicio,
        DateOnly fim,
        int pagina,
        int tamanhoPagina,
        CancellationToken cancellationToken = default)
    {
        // id como desempate: a data sozinha não é única e a paginação ficaria instável.
        var sql = $"""
            SELECT {ColunasSelect}
            FROM lancamentos
            WHERE comerciante_id = @comercianteId
              AND data_competencia BETWEEN @inicio AND @fim
            ORDER BY data_competencia DESC, id DESC
            LIMIT @tamanhoPagina OFFSET @deslocamento;
            """;

        var conexao = await sessao.ObterConexaoAsync(cancellationToken);

        var linhas = await conexao.QueryAsync<LancamentoLinha>(new CommandDefinition(sql, new
        {
            comercianteId,
            inicio,
            fim,
            tamanhoPagina,
            deslocamento = (pagina - 1) * tamanhoPagina,
        }, sessao.Transacao, cancellationToken: cancellationToken));

        return [.. linhas.Select(l => l.ParaDominio())];
    }

    public async Task<int> ContarPorPeriodoAsync(
        Guid comercianteId,
        DateOnly inicio,
        DateOnly fim,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT count(*)
            FROM lancamentos
            WHERE comerciante_id = @comercianteId
              AND data_competencia BETWEEN @inicio AND @fim;
            """;

        var conexao = await sessao.ObterConexaoAsync(cancellationToken);

        return await conexao.ExecuteScalarAsync<int>(new CommandDefinition(
            sql, new { comercianteId, inicio, fim }, sessao.Transacao, cancellationToken: cancellationToken));
    }

    private async Task<Lancamento?> ConsultarUmAsync(
        string sql,
        object parametros,
        CancellationToken cancellationToken)
    {
        var conexao = await sessao.ObterConexaoAsync(cancellationToken);

        var linha = await conexao.QueryFirstOrDefaultAsync<LancamentoLinha>(new CommandDefinition(
            sql, parametros, sessao.Transacao, cancellationToken: cancellationToken));

        return linha?.ParaDominio();
    }

    /// <summary>
    /// Projeção crua da tabela. Existe para manter a entidade de domínio livre
    /// de construtor público sem parâmetros e de setters exigidos por mapeador.
    /// </summary>
    private sealed record LancamentoLinha
    {
        public Guid Id { get; init; }
        public Guid ComercianteId { get; init; }
        public string Tipo { get; init; } = string.Empty;
        public decimal Valor { get; init; }
        public string Moeda { get; init; } = string.Empty;
        public DateOnly DataCompetencia { get; init; }
        public string? Descricao { get; init; }
        public Guid? EstornoDeId { get; init; }
        public string ChaveIdempotencia { get; init; } = string.Empty;
        public string HashPayload { get; init; } = string.Empty;
        public DateTimeOffset CriadoEm { get; init; }

        public Lancamento ParaDominio()
        {
            if (!TipoLancamentoExtensions.TentarConverter(Tipo, out var tipo))
            {
                throw new InvalidOperationException(
                    $"Tipo de lançamento inválido no banco: '{Tipo}' (lançamento {Id}).");
            }

            return Lancamento.Reidratar(
                id: Id,
                comercianteId: ComercianteId,
                tipo: tipo,
                valor: Dinheiro.Criar(Valor, Domain.Moeda.Criar(Moeda)),
                dataCompetencia: DataCompetencia,
                descricao: Descricao,
                estornoDeId: EstornoDeId,
                chaveIdempotencia: ChaveIdempotencia,
                hashPayload: HashPayload,
                criadoEm: CriadoEm);
        }
    }
}
