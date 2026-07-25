namespace Lancamentos.Domain.Eventos;

/// <summary>
/// Envelope comum a todo evento publicado pelo serviço.
/// </summary>
public abstract record EventoDeDominio
{
    /// <summary>Identidade do evento. Chave de dedupe no consumidor.</summary>
    public Guid EventId { get; init; } = Guid.CreateVersion7();

    /// <summary>Versão do contrato do payload.</summary>
    public abstract int Version { get; }

    /// <summary>Nome do tipo do evento, usado no roteamento e na desserialização.</summary>
    public abstract string EventType { get; }

    /// <summary>Momento em que o fato ocorreu, em UTC.</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>
    /// Identificador de rastreamento propagado desde a requisição HTTP original.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>Chave de roteamento no broker.</summary>
    public abstract string RoutingKey { get; }

    /// <summary>Agregado que originou o evento.</summary>
    public abstract Guid AgregadoId { get; }

    /// <summary>Comerciante dono do agregado. Coluna própria na outbox, para replay.</summary>
    public abstract Guid ComercianteId { get; }

    /// <summary>
    /// Competência do fato. Coluna própria na outbox porque o replay recorta por
    /// competência, não por data física de gravação.
    /// </summary>
    public abstract DateOnly DataCompetencia { get; }
}
