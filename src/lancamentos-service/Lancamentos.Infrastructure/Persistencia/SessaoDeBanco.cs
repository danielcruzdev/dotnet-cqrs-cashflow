using Npgsql;

namespace Lancamentos.Infrastructure.Persistencia;

/// <summary>
/// Conexão e transação corrente, compartilhadas por todos os repositórios
/// dentro do escopo de uma requisição.
/// </summary>
/// <remarks>
/// <para>
/// É este objeto que torna o Outbox Pattern possível. Sem ele, cada repositório
/// abriria sua própria conexão e o <c>INSERT</c> do lançamento e o da outbox
/// cairiam em transações diferentes — exatamente a inconsistência que o padrão
/// existe para evitar.
/// </para>
/// <para>
/// Registrado como <c>Scoped</c>: uma conexão por requisição, aberta sob demanda
/// e devolvida ao pool no fim. Consultas fora de transação usam a mesma conexão,
/// sem custo adicional.
/// </para>
/// </remarks>
public sealed class SessaoDeBanco(NpgsqlDataSource dataSource) : IAsyncDisposable
{
    private NpgsqlConnection? _conexao;

    /// <summary>Transação corrente, ou <c>null</c> se não houver uma aberta.</summary>
    public NpgsqlTransaction? Transacao { get; private set; }

    public bool EmTransacao => Transacao is not null;

    public async ValueTask<NpgsqlConnection> ObterConexaoAsync(CancellationToken cancellationToken = default)
    {
        _conexao ??= await dataSource.OpenConnectionAsync(cancellationToken);
        return _conexao;
    }

    public async Task IniciarTransacaoAsync(CancellationToken cancellationToken = default)
    {
        if (EmTransacao)
        {
            throw new InvalidOperationException(
                "Já existe uma transação aberta nesta sessão. Transações aninhadas não são suportadas.");
        }

        var conexao = await ObterConexaoAsync(cancellationToken);
        Transacao = await conexao.BeginTransactionAsync(cancellationToken);
    }

    public async Task ConfirmarAsync(CancellationToken cancellationToken = default)
    {
        if (Transacao is null)
        {
            throw new InvalidOperationException("Não há transação aberta para confirmar.");
        }

        await Transacao.CommitAsync(cancellationToken);
        await DescartarTransacaoAsync();
    }

    public async Task DesfazerAsync(CancellationToken cancellationToken = default)
    {
        if (Transacao is null)
        {
            return;
        }

        await Transacao.RollbackAsync(cancellationToken);
        await DescartarTransacaoAsync();
    }

    private async Task DescartarTransacaoAsync()
    {
        if (Transacao is not null)
        {
            await Transacao.DisposeAsync();
            Transacao = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Transação ainda aberta aqui significa que ninguém confirmou: o caminho
        // seguro é desfazer. Na prática o IUnitOfWork já garante isso, mas o
        // descarte é a última linha de defesa contra transação vazada.
        await DescartarTransacaoAsync();

        if (_conexao is not null)
        {
            await _conexao.DisposeAsync();
            _conexao = null;
        }
    }
}
