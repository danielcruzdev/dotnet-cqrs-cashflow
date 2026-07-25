namespace Lancamentos.Domain;

/// <summary>
/// Um lançamento no livro-caixa do comerciante: um débito ou um crédito.
/// </summary>
public sealed class Lancamento
{
    public const int TamanhoMaximoDescricao = 500;
    public const int TamanhoMaximoChaveIdempotencia = 100;
    private const int TamanhoHashSha256Hex = 64;

    public Guid Id { get; }

    public Guid ComercianteId { get; }

    public TipoLancamento Tipo { get; }

    public Dinheiro Valor { get; }

    /// <summary>Dia contábil ao qual o lançamento pertence, no fuso do comerciante.</summary>
    public DateOnly DataCompetencia { get; }

    public string? Descricao { get; }

    /// <summary>Preenchido quando este lançamento é o estorno de outro.</summary>
    public Guid? EstornoDeId { get; }

    public string ChaveIdempotencia { get; }

    /// <summary>SHA-256 do payload da requisição, em hexadecimal minúsculo.</summary>
    public string HashPayload { get; }

    /// <summary>Instante físico do registro, em UTC. Base do cálculo de lag de consistência.</summary>
    public DateTimeOffset CriadoEm { get; }

    public bool EhEstorno => EstornoDeId.HasValue;

    private Lancamento(
        Guid id,
        Guid comercianteId,
        TipoLancamento tipo,
        Dinheiro valor,
        DateOnly dataCompetencia,
        string? descricao,
        Guid? estornoDeId,
        string chaveIdempotencia,
        string hashPayload,
        DateTimeOffset criadoEm)
    {
        Id = id;
        ComercianteId = comercianteId;
        Tipo = tipo;
        Valor = valor;
        DataCompetencia = dataCompetencia;
        Descricao = descricao;
        EstornoDeId = estornoDeId;
        ChaveIdempotencia = chaveIdempotencia;
        HashPayload = hashPayload;
        CriadoEm = criadoEm;
    }

    /// <summary>
    /// Registra um novo lançamento, validando todas as regras de negócio.
    /// </summary>
    /// <exception cref="DominioException">Se alguma regra for violada.</exception>
    public static Lancamento Criar(
        Guid comercianteId,
        TipoLancamento tipo,
        Dinheiro valor,
        DateOnly dataCompetencia,
        string? descricao,
        string chaveIdempotencia,
        string hashPayload,
        IRelogio relogio)
    {
        ArgumentNullException.ThrowIfNull(relogio);

        if (comercianteId == Guid.Empty)
        {
            throw DominioException.CampoObrigatorio(nameof(comercianteId));
        }

        ValidarChaveIdempotencia(chaveIdempotencia);
        ValidarHashPayload(hashPayload);
        ValidarDescricao(descricao);

        // A comparação é contra HOJE NO FUSO DO COMERCIANTE, não contra
        // DateTime.UtcNow.Date. Às 22h de São Paulo os dois já divergem, e o
        // lançamento legítimo da noite seria rejeitado como "futuro".
        var hoje = relogio.HojeParaComerciante;

        if (dataCompetencia > hoje)
        {
            throw DominioException.DataCompetenciaFutura(dataCompetencia, hoje);
        }

        return new Lancamento(
            id: Guid.CreateVersion7(),
            comercianteId: comercianteId,
            tipo: tipo,
            valor: valor,
            dataCompetencia: dataCompetencia,
            descricao: string.IsNullOrWhiteSpace(descricao) ? null : descricao.Trim(),
            estornoDeId: null,
            chaveIdempotencia: chaveIdempotencia.Trim(),
            hashPayload: hashPayload.ToLowerInvariant(),
            criadoEm: relogio.AgoraUtc);
    }

    /// <summary>
    /// Produz o lançamento compensatório deste: mesmo valor, mesma data de
    /// competência, tipo invertido.
    /// </summary>
    /// <exception cref="DominioException">Se este lançamento já for um estorno.</exception>
    public Lancamento Estornar(string chaveIdempotencia, string hashPayload, IRelogio relogio)
    {
        ArgumentNullException.ThrowIfNull(relogio);

        // Estornar um estorno é ambíguo: o usuário quer desfazer a correção ou
        // repetir o lançamento original? Em vez de adivinhar, o domínio recusa
        // e obriga a intenção a ser explícita.
        if (EhEstorno)
        {
            throw DominioException.EstornoDeEstorno(Id);
        }

        ValidarChaveIdempotencia(chaveIdempotencia);
        ValidarHashPayload(hashPayload);

        return new Lancamento(
            id: Guid.CreateVersion7(),
            comercianteId: ComercianteId,
            tipo: Tipo.Inverso(),
            valor: Valor,
            dataCompetencia: DataCompetencia,
            descricao: $"Estorno do lançamento {Id}",
            estornoDeId: Id,
            chaveIdempotencia: chaveIdempotencia.Trim(),
            hashPayload: hashPayload.ToLowerInvariant(),
            criadoEm: relogio.AgoraUtc);
    }

    /// <summary>
    /// Reidrata a entidade a partir do estado persistido, sem revalidar.
    /// </summary>
    public static Lancamento Reidratar(
        Guid id,
        Guid comercianteId,
        TipoLancamento tipo,
        Dinheiro valor,
        DateOnly dataCompetencia,
        string? descricao,
        Guid? estornoDeId,
        string chaveIdempotencia,
        string hashPayload,
        DateTimeOffset criadoEm) =>
        new(id, comercianteId, tipo, valor, dataCompetencia, descricao,
            estornoDeId, chaveIdempotencia, hashPayload, criadoEm);

    private static void ValidarChaveIdempotencia(string chave)
    {
        if (string.IsNullOrWhiteSpace(chave))
        {
            throw DominioException.CampoObrigatorio(nameof(chave));
        }

        if (chave.Trim().Length > TamanhoMaximoChaveIdempotencia)
        {
            throw DominioException.CampoAcimaDoLimite(
                nameof(chave), TamanhoMaximoChaveIdempotencia);
        }
    }

    private static void ValidarHashPayload(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash) || hash.Length != TamanhoHashSha256Hex)
        {
            throw DominioException.CampoObrigatorio(nameof(hash));
        }
    }

    private static void ValidarDescricao(string? descricao)
    {
        if (descricao is not null && descricao.Trim().Length > TamanhoMaximoDescricao)
        {
            throw DominioException.CampoAcimaDoLimite(
                nameof(descricao), TamanhoMaximoDescricao);
        }
    }
}
