namespace Consolidado.Domain;

/// <summary>Consulta rejeitada por regra. O Codigo é o contrato com o cliente.</summary>
public sealed class ConsultaInvalidaException(string codigo, string mensagem) : Exception(mensagem)
{
    public string Codigo { get; } = codigo;
}
