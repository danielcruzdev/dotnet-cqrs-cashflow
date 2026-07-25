using System.Data;
using System.Globalization;
using Dapper;

namespace Lancamentos.Infrastructure.Persistencia;

/// <summary>
/// Mapeia <see cref="DateOnly"/> para a coluna <c>date</c> do PostgreSQL.
/// </summary>
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
