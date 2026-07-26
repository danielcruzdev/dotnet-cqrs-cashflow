using System.Security.Claims;
using Lancamentos.Api.Contratos;
using Lancamentos.Application.Abstracoes;
using Lancamentos.Application.ConsultarLancamentos;
using Lancamentos.Application.CriarLancamento;
using Lancamentos.Application.EstornarLancamento;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Lancamentos.Api.Endpoints;

public static class LancamentosEndpoints
{
    private const string HeaderIdempotencia = "Idempotency-Key";
    private const string HeaderCorrelacao = "X-Correlation-Id";

    public static IEndpointRouteBuilder MapearLancamentos(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/lancamentos")
            .WithTags("Lançamentos")
            .RequireAuthorization();

        grupo.MapPost("/", CriarAsync)
            .WithName("CriarLancamento")
            .WithSummary("Registra um lançamento no livro-caixa");

        grupo.MapPost("/{id:guid}/estorno", EstornarAsync)
            .WithName("EstornarLancamento")
            .WithSummary("Registra o lançamento compensatório de um lançamento existente");

        grupo.MapGet("/", ListarAsync)
            .WithName("ListarLancamentos")
            .WithSummary("Lista lançamentos de um período");

        grupo.MapGet("/{id:guid}", ObterAsync)
            .WithName("ObterLancamento")
            .WithSummary("Obtém um lançamento por id");

        return app;
    }

    private static async Task<IResult> CriarAsync(
        CriarLancamentoRequest request,
        HttpContext contexto,
        [FromServices] ICommandHandler<CriarLancamentoCommand, ResultadoCriarLancamento> handler,
        CancellationToken cancellationToken)
    {
        if (!contexto.User.EhDono(request.ComercianteId))
        {
            return Autorizacao.AcessoNegado();
        }

        if (!TentarObterChaveIdempotencia(contexto, out var chave))
        {
            return ChaveIdempotenciaAusente();
        }

        var comando = new CriarLancamentoCommand
        {
            ComercianteId = request.ComercianteId,
            Tipo = request.Tipo,
            Valor = request.Valor,
            Moeda = request.Moeda,
            DataCompetencia = request.DataCompetencia,
            Descricao = request.Descricao,
            ChaveIdempotencia = chave,
            CorrelationId = ObterCorrelationId(contexto),
        };

        var resultado = await handler.ExecutarAsync(comando, cancellationToken);
        var corpo = LancamentoResponse.De(resultado.Lancamento);

        return resultado.Status switch
        {
            StatusCriacao.Criado => TypedResults.Created($"/api/lancamentos/{corpo.Id}", corpo),

            // Nada foi criado nesta requisição.
            StatusCriacao.JaRegistrado => TypedResults.Ok(corpo),

            StatusCriacao.ConflitoDeChave => TypedResults.Problem(
                title: "Chave de idempotência já utilizada",
                detail: "Esta chave já foi usada para um lançamento com conteúdo diferente.",
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?> { ["lancamentoExistenteId"] = corpo.Id }),

            _ => throw new InvalidOperationException($"Status não tratado: {resultado.Status}"),
        };
    }

    private static async Task<IResult> EstornarAsync(
        Guid id,
        [FromQuery] Guid comercianteId,
        HttpContext contexto,
        [FromServices] ICommandHandler<EstornarLancamentoCommand, ResultadoEstorno> handler,
        CancellationToken cancellationToken)
    {
        if (!contexto.User.EhDono(comercianteId))
        {
            return Autorizacao.AcessoNegado();
        }

        if (!TentarObterChaveIdempotencia(contexto, out var chave))
        {
            return ChaveIdempotenciaAusente();
        }

        var resultado = await handler.ExecutarAsync(new EstornarLancamentoCommand
        {
            ComercianteId = comercianteId,
            LancamentoId = id,
            ChaveIdempotencia = chave,
            CorrelationId = ObterCorrelationId(contexto),
        }, cancellationToken);

        return resultado.Status switch
        {
            StatusEstorno.Criado => TypedResults.Created(
                $"/api/lancamentos/{resultado.Estorno!.Id}",
                LancamentoResponse.De(resultado.Estorno)),

            StatusEstorno.JaRegistrado => TypedResults.Ok(
                LancamentoResponse.De(resultado.Estorno!)),

            StatusEstorno.OriginalNaoEncontrado => TypedResults.Problem(
                title: "Lançamento não encontrado",
                detail: $"Não existe lançamento {id} para este comerciante.",
                statusCode: StatusCodes.Status404NotFound),

            StatusEstorno.JaEstornado => TypedResults.Problem(
                title: "Lançamento já estornado",
                detail: "Um lançamento só pode ser estornado uma vez.",
                statusCode: StatusCodes.Status409Conflict),

            StatusEstorno.ConflitoDeChave => TypedResults.Problem(
                title: "Chave de idempotência já utilizada",
                detail: "Esta chave já foi usada para outra operação deste comerciante.",
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?>
                {
                    ["lancamentoExistenteId"] = resultado.Estorno!.Id,
                }),

            _ => throw new InvalidOperationException($"Status não tratado: {resultado.Status}"),
        };
    }

    private static async Task<Results<Ok<PaginaResponse<LancamentoResponse>>, ProblemHttpResult>> ListarAsync(
        [FromQuery] Guid comercianteId,
        [FromQuery] DateOnly dataInicio,
        [FromQuery] DateOnly dataFim,
        ClaimsPrincipal usuario,
        [FromServices] IQueryHandler<ListarLancamentosQuery, PaginaDeLancamentos> handler,
        CancellationToken cancellationToken,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanhoPagina = ListarLancamentosQuery.TamanhoPaginaPadrao)
    {
        if (!usuario.EhDono(comercianteId))
        {
            return Autorizacao.AcessoNegado();
        }

        var resultado = await handler.ExecutarAsync(new ListarLancamentosQuery
        {
            ComercianteId = comercianteId,
            DataInicio = dataInicio,
            DataFim = dataFim,
            Pagina = pagina,
            TamanhoPagina = tamanhoPagina,
        }, cancellationToken);

        return TypedResults.Ok(new PaginaResponse<LancamentoResponse>
        {
            Itens = [.. resultado.Itens.Select(LancamentoResponse.De)],
            Pagina = resultado.Pagina,
            TamanhoPagina = resultado.TamanhoPagina,
            Total = resultado.Total,
            TotalDePaginas = resultado.TotalDePaginas,
        });
    }

    private static async Task<Results<Ok<LancamentoResponse>, NotFound, ProblemHttpResult>> ObterAsync(
        Guid id,
        [FromQuery] Guid comercianteId,
        ClaimsPrincipal usuario,
        [FromServices] IQueryHandler<ObterLancamentoPorIdQuery, Domain.Lancamento?> handler,
        CancellationToken cancellationToken)
    {
        if (!usuario.EhDono(comercianteId))
        {
            return Autorizacao.AcessoNegado();
        }

        var lancamento = await handler.ExecutarAsync(
            new ObterLancamentoPorIdQuery(comercianteId, id), cancellationToken);

        return lancamento is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(LancamentoResponse.De(lancamento));
    }

    private static bool TentarObterChaveIdempotencia(HttpContext contexto, out string chave)
    {
        chave = contexto.Request.Headers[HeaderIdempotencia].ToString();
        return !string.IsNullOrWhiteSpace(chave);
    }

    private static ProblemHttpResult ChaveIdempotenciaAusente() => TypedResults.Problem(
        title: "Header Idempotency-Key obrigatório",
        detail: "Toda escrita exige o header Idempotency-Key para tornar o retry seguro.",
        statusCode: StatusCodes.Status400BadRequest);

    private static Guid ObterCorrelationId(HttpContext contexto) =>
        Guid.TryParse(contexto.Request.Headers[HeaderCorrelacao], out var id)
            ? id
            : Guid.CreateVersion7();
}
