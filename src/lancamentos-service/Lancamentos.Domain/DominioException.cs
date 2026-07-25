namespace Lancamentos.Domain;

/// <summary>
/// Violação de uma regra de negócio do domínio.
/// </summary>
public sealed class DominioException(string codigo, string mensagem) : Exception(mensagem)
{
    public string Codigo { get; } = codigo;

    public static DominioException ValorNaoPositivo(decimal valor) =>
        new("lancamento.valor_nao_positivo",
            $"O valor deve ser maior que zero. Recebido: {valor}. O sinal da operação " +
            "vem do tipo (DEBITO/CREDITO), nunca de valor negativo.");

    public static DominioException PrecisaoMonetariaInvalida(decimal valor, int casas) =>
        new("lancamento.precisao_invalida",
            $"O valor {valor} tem mais de {casas} casas decimais. Arredondamento " +
            "implícito em livro-caixa produz divergência acumulada no saldo.");

    public static DominioException MoedaNaoSuportada(string codigo) =>
        new("lancamento.moeda_nao_suportada",
            $"A moeda '{codigo}' não é suportada. São aceitos códigos ISO 4217 de " +
            "duas casas decimais — ver Moeda.Suportadas.");

    public static DominioException DataCompetenciaFutura(DateOnly informada, DateOnly hoje) =>
        new("lancamento.data_competencia_futura",
            $"A data de competência {informada:yyyy-MM-dd} é futura em relação a " +
            $"{hoje:yyyy-MM-dd} no fuso do comerciante. Fluxo de caixa registra o que " +
            "já aconteceu; agendamento é outro caso de uso.");

    public static DominioException EstornoDeEstorno(Guid lancamentoId) =>
        new("lancamento.estorno_de_estorno",
            $"O lançamento {lancamentoId} já é um estorno e não pode ser estornado. " +
            "Para reverter um estorno, registre um novo lançamento equivalente ao original.");

    public static DominioException CampoObrigatorio(string campo) =>
        new("lancamento.campo_obrigatorio", $"O campo '{campo}' é obrigatório.");

    public static DominioException CampoAcimaDoLimite(string campo, int limite) =>
        new("lancamento.campo_acima_do_limite",
            $"O campo '{campo}' excede o limite de {limite} caracteres.");
}
