namespace Lancamentos.Domain;

/// <summary>
/// Acesso ao tempo, expresso nos termos do domínio.
/// </summary>
public interface IRelogio
{
    /// <summary>Instante atual em UTC. Para auditoria e cálculo de lag, nunca para agregação.</summary>
    DateTimeOffset AgoraUtc { get; }

    /// <summary>Dia civil corrente no fuso do comerciante. É o teto da data de competência.</summary>
    DateOnly HojeParaComerciante { get; }
}
