using SpotifyDataAnalysis.SharedKernel.Domain;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;

/// <summary>
/// <b>ISRC</b> (International Standard Recording Code) — o identificador global e <b>estável</b> de uma
/// gravação, definido pela norma ISO 3901. A Spotify o expõe em <c>external_ids.isrc</c>.
///
/// <para>Vale como value object (e não como <c>string?</c> solta) por dois motivos concretos:</para>
/// <list type="number">
///   <item>tem <b>formato próprio</b> — 12 caracteres: 2 letras de país + 3 alfanuméricos de registrante +
///   2 dígitos de ano + 5 dígitos de designação (<c>BRBMG0300729</c>). Uma string sem validação deixaria
///   lixo entrar no agregado;</item>
///   <item>precisa de <b>normalização</b> para servir de chave de casamento: o código circula com e sem
///   hifens (<c>BR-BMG-03-00729</c>) e em caixa mista, e as duas formas designam a mesma gravação.
///   Normalizar aqui — e só aqui — impede que cada consumidor invente a sua.</item>
/// </list>
///
/// <para>É o identificador que permitirá casar a mesma gravação entre fontes diferentes sem depender de
/// texto (o fallback frágil de <see cref="TrackMatchKey"/>).</para>
/// </summary>
public sealed class Isrc : ValueObject
{
    /// <summary>Comprimento do código já normalizado (sem hifens).</summary>
    public const int Length = 12;

    private Isrc(string value) => Value = value;

    /// <summary>O código normalizado: 12 caracteres, maiúsculas, sem hifens nem espaços.</summary>
    public string Value { get; }

    /// <summary>
    /// Cria o ISRC a partir do código cru, validando o formato. Use quando a ausência ou a invalidez do
    /// código for um <b>erro</b>; para dado vindo de fonte externa, prefira <see cref="TryParse"/>.
    /// </summary>
    public static Isrc Of(string? value)
    {
        return TryParse(value, out Isrc? isrc)
            ? isrc!
            : throw new DomainException($"ISRC inválido: '{value}'. Esperado o formato ISO 3901 (ex.: BRBMG0300729).");
    }

    /// <summary>
    /// Tentativa <b>tolerante</b> de interpretar o código: devolve <see langword="false"/> quando o valor
    /// está ausente ou fora do formato, sem lançar.
    ///
    /// <para>É esta a porta usada pela ingestão. A API do Spotify simplesmente omite <c>external_ids.isrc</c>
    /// em parte do catálogo (faixas locais, lançamentos sem código registrado), e uma faixa isolada com o
    /// campo malformado não pode derrubar o ciclo de coleta inteiro — o ISRC é um <b>enriquecimento</b>
    /// opcional, não uma invariante da faixa.</para>
    /// </summary>
    public static bool TryParse(string? value, out Isrc? isrc)
    {
        isrc = null;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        string normalized = Normalize(value);
        if (!HasValidFormat(normalized))
            return false;

        isrc = new Isrc(normalized);
        return true;
    }

    /// <summary>Remove hifens/espaços (separadores puramente cosméticos) e uniformiza a caixa.</summary>
    private static string Normalize(string value)
        => string.Concat(value.Where(character => character is not ('-' or ' '))).ToUpperInvariant();

    /// <summary>
    /// País (2 letras) + registrante (3 alfanuméricos) + ano (2 dígitos) + designação (5 dígitos).
    /// A validação é <b>estrutural</b>: não há como conferir se o código foi de fato emitido.
    /// </summary>
    private static bool HasValidFormat(string value)
    {
        if (value.Length != Length)
            return false;

        return IsAsciiLetter(value[0]) && IsAsciiLetter(value[1])
            && value.Skip(2).Take(3).All(IsAsciiLetterOrDigit)
            && value.Skip(5).All(char.IsAsciiDigit);
    }

    private static bool IsAsciiLetter(char character) => char.IsAsciiLetterUpper(character);

    private static bool IsAsciiLetterOrDigit(char character)
        => char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
