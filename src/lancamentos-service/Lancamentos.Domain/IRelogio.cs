namespace Lancamentos.Domain;

/// <summary>
/// Acesso ao tempo, expresso nos termos do domínio.
/// </summary>
/// <remarks>
/// <para>
/// O domínio não pergunta "que horas são em UTC" — pergunta <b>"que dia é hoje
/// para o comerciante"</b>. São perguntas diferentes, e confundi-las é o bug
/// clássico deste domínio: um lançamento feito às 22h de 25/07 em São Paulo é
/// 01h de 26/07 em UTC, e cairia no dia contábil errado.
/// </para>
/// <para>
/// A interface vive no domínio porque a regra ("o dia contábil é o dia civil no
/// fuso do comerciante") é de negócio. A implementação vive na infraestrutura e
/// se apoia em <c>TimeProvider</c>, a abstração de tempo da própria plataforma —
/// o que permite usar <c>FakeTimeProvider</c> nos testes em vez de um mock
/// artesanal.
/// </para>
/// </remarks>
public interface IRelogio
{
    /// <summary>Instante atual em UTC. Para auditoria e cálculo de lag, nunca para agregação.</summary>
    DateTimeOffset AgoraUtc { get; }

    /// <summary>Dia civil corrente no fuso do comerciante. É o teto da data de competência.</summary>
    DateOnly HojeParaComerciante { get; }
}
