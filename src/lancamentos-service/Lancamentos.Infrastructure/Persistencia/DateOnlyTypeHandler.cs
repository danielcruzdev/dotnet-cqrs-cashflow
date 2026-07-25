using System.Data;
using System.Globalization;
using Dapper;

namespace Lancamentos.Infrastructure.Persistencia;

/// <summary>
/// Mapeia <see cref="DateOnly"/> para a coluna <c>date</c> do PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// O Npgsql mapeia <see cref="DateOnly"/> nativamente e as versões recentes do
/// Dapper também. O handler está aqui como garantia explícita: a alternativa,
/// caso alguma das duas bibliotecas não reconheça o tipo, seria o Dapper cair
/// no caminho de <c>DateTime</c> e reintroduzir componente de hora numa coluna
/// que representa <b>dia contábil</b> — exatamente a confusão entre instante e
/// dia que o domínio se esforça para eliminar.
/// </para>
/// <para>
/// Registrado uma única vez na composição da infraestrutura.
/// </para>
/// </remarks>
public sealed class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
{
    public override void SetValue(IDbDataParameter parameter, DateOnly value)
    {
        parameter.DbType = DbType.Date;
        parameter.Value = value;
    }

    public override DateOnly Parse(object value) => value switch
    {
        DateOnly data => data,
        DateTime dataHora => DateOnly.FromDateTime(dataHora),
        string texto => DateOnly.Parse(texto, CultureInfo.InvariantCulture),
        _ => throw new InvalidCastException(
            $"Não é possível converter '{value?.GetType().Name ?? "null"}' para DateOnly."),
    };
}
