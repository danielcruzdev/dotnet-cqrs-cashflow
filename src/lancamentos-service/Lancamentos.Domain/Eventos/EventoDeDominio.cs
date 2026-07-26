using System.Text.Json.Serialization;

namespace Lancamentos.Domain.Eventos;

/// <summary>Envelope comum a todo evento publicado pelo serviço.</summary>
public abstract record EventoDeDominio
{
    /// <summary>Chave de dedupe no consumidor — o broker entrega at-least-once.</summary>
    public Guid EventId { get; init; } = Guid.CreateVersion7();

    public abstract int Version { get; }

    public abstract string EventType { get; }

    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>Propagado da requisição HTTP original até o consumidor.</summary>
    public required Guid CorrelationId { get; init; }

    public required Guid AgregadoId { get; init; }

    public required Guid ComercianteId { get; init; }

    /// <summary>Coluna própria na outbox: o replay recorta por competência.</summary>
    public required DateOnly DataCompetencia { get; init; }

    /// <summary>Roteamento é decisão de infraestrutura, não parte do contrato.</summary>
    [JsonIgnore]
    public abstract string RoutingKey { get; }
}
