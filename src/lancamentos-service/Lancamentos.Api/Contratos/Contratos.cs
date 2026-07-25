using System.ComponentModel.DataAnnotations;
using Lancamentos.Domain;

namespace Lancamentos.Api.Contratos;

public sealed record CriarLancamentoRequest
{
    [Required]
    public Guid ComercianteId { get; init; }

    [Required]
    [AllowedValues("DEBITO", "CREDITO")]
    public string Tipo { get; init; } = string.Empty;

    // Sem as flags, os limites seriam parseados na cultura corrente (pt-BR quebra).
    [Range(typeof(decimal), "0.01", "9999999999999999.99",
        ParseLimitsInInvariantCulture = true,
        ConvertValueInInvariantCulture = true)]
    public decimal Valor { get; init; }

    [Required]
    [StringLength(3, MinimumLength = 3)]
    public string Moeda { get; init; } = "BRL";

    [Required]
    public DateOnly DataCompetencia { get; init; }

    [StringLength(Lancamento.TamanhoMaximoDescricao)]
    public string? Descricao { get; init; }
}

public sealed record LancamentoResponse
{
    public required Guid Id { get; init; }
    public required Guid ComercianteId { get; init; }
    public required string Tipo { get; init; }
    public required decimal Valor { get; init; }
    public required string Moeda { get; init; }
    public required DateOnly DataCompetencia { get; init; }
    public string? Descricao { get; init; }
    public Guid? EstornoDeId { get; init; }
    public required DateTimeOffset CriadoEm { get; init; }

    public static LancamentoResponse De(Lancamento l) => new()
    {
        Id = l.Id,
        ComercianteId = l.ComercianteId,
        Tipo = l.Tipo.ParaPersistencia(),
        Valor = l.Valor.Valor,
        Moeda = l.Valor.Moeda.Codigo,
        DataCompetencia = l.DataCompetencia,
        Descricao = l.Descricao,
        EstornoDeId = l.EstornoDeId,
        CriadoEm = l.CriadoEm,
    };
}

public sealed record PaginaResponse<T>
{
    public required IReadOnlyList<T> Itens { get; init; }
    public required int Pagina { get; init; }
    public required int TamanhoPagina { get; init; }
    public required int Total { get; init; }
    public required int TotalDePaginas { get; init; }
}
