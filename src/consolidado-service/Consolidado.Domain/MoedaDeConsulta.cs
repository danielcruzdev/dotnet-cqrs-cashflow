namespace Consolidado.Domain;

/// <summary>
/// Valida e normaliza o código de moeda que chega na query string.
/// </summary>
public static class MoedaDeConsulta
{
    /// <exception cref="ConsultaInvalidaException">Se o código não for três letras.</exception>
    public static string Normalizar(string? moeda)
    {
        // Sem isso, "?moeda=zzzzz" devolve 200 com zeros — como se o comerciante
        // não tivesse movimento — e ainda cria uma chave inútil no Redis. E "brl"
        // minúsculo nunca encontra a linha, que está gravada como "BRL".
        var normalizada = moeda?.Trim().ToUpperInvariant();

        if (normalizada is not { Length: 3 } || !normalizada.All(char.IsAsciiLetterUpper))
        {
            throw new ConsultaInvalidaException(
                "consulta.moeda_invalida",
                $"A moeda '{moeda}' é inválida. Informe um código ISO 4217 de três letras.");
        }

        return normalizada;
    }
}
