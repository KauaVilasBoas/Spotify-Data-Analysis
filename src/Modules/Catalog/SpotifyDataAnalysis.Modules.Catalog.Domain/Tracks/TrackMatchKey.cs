using System.Globalization;
using System.Text;
using SpotifyDataAnalysis.SharedKernel.Domain;

namespace SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;

/// <summary>
/// Chave normalizada de <b>"artista principal + título"</b> usada para casar uma faixa do catálogo com uma
/// linha do dataset externo quando o <c>track_id</c> não bate (o fallback do E1.4).
///
/// O casamento textual falha por diferenças cosméticas — acento, caixa, pontuação e principalmente os
/// sufixos editoriais que o Spotify carrega e o dataset não (<c>"Bohemian Rhapsody - Remastered 2011"</c>
/// vs <c>"Bohemian Rhapsody"</c>). A normalização abaixo remove exatamente essas diferenças:
///
/// <list type="number">
///   <item>corta o sufixo editorial após <c>" - "</c> (Remastered, Live, Radio Edit, …);</item>
///   <item>remove trechos entre parênteses/colchetes (<c>"(feat. X)"</c>, <c>"[Bonus Track]"</c>);</item>
///   <item>remove acentuação, baixa a caixa e descarta pontuação;</item>
///   <item>colapsa espaços.</item>
/// </list>
///
/// <b>Trade-off consciente:</b> é uma chave de <i>heurística</i>, não uma identidade — pode colidir (duas
/// faixas homônimas do mesmo artista) e por isso nunca é única no banco nem substitui o <c>track_id</c>;
/// só entra em cena quando o casamento por id já falhou.
/// </summary>
public sealed class TrackMatchKey : ValueObject
{
    private const string EditorialSuffixSeparator = " - ";

    private TrackMatchKey(string value) => Value = value;

    /// <summary>A chave no formato <c>"artista|titulo"</c>, já normalizada.</summary>
    public string Value { get; }

    /// <summary>
    /// <see langword="true"/> quando não sobrou texto útil para casar (título e artista vazios após a
    /// normalização) — uma chave vazia NUNCA deve ser usada para casar, sob pena de agrupar faixas sem relação.
    /// </summary>
    public bool IsEmpty => Value == "|";

    /// <summary>Monta a chave a partir do título da faixa e do nome do artista principal.</summary>
    public static TrackMatchKey From(string? trackName, string? primaryArtistName)
        => new($"{Normalize(primaryArtistName)}|{Normalize(trackName)}");

    /// <summary>
    /// Reidrata a chave a partir do valor já normalizado que está persistido. Reaplicar
    /// <see cref="From"/> aqui seria <b>errado</b>: o separador <c>|</c> não é alfanumérico e a normalização
    /// o descartaria, corrompendo a chave a cada round-trip do banco.
    /// </summary>
    public static TrackMatchKey FromNormalized(string value) => new(value ?? "|");

    /// <summary>
    /// Monta a chave a partir do campo <c>artists</c> do dataset Kaggle, que lista os artistas separados por
    /// <c>;</c> — o primeiro é o principal, e é o único que participa da chave (a ordem dos demais varia
    /// entre as fontes).
    /// </summary>
    public static TrackMatchKey FromArtistList(string? trackName, string? semicolonSeparatedArtists)
    {
        string? primary = semicolonSeparatedArtists?.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        return From(trackName, primary);
    }

    private static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        string working = text;

        int suffixIndex = working.IndexOf(EditorialSuffixSeparator, StringComparison.Ordinal);
        if (suffixIndex > 0)
            working = working[..suffixIndex];

        working = RemoveBracketedSegments(working);
        working = RemoveDiacritics(working.ToLowerInvariant());

        return KeepAlphanumericAndCollapseSpaces(working);
    }

    private static string RemoveBracketedSegments(string text)
    {
        var builder = new StringBuilder(text.Length);
        int depth = 0;

        foreach (char character in text)
        {
            if (character is '(' or '[')
                depth++;
            else if (character is ')' or ']')
                depth = Math.Max(0, depth - 1);
            else if (depth == 0)
                builder.Append(character);
        }

        return builder.ToString();
    }

    private static string RemoveDiacritics(string text)
    {
        string decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (char character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Apóstrofos são <b>apagados</b>, não trocados por espaço: <c>"Don't"</c> e <c>"Dont"</c> precisam
    /// produzir a mesma chave (o dataset costuma omitir a aspa), e trocá-la por espaço geraria
    /// <c>"don t"</c> ≠ <c>"dont"</c>. Vale para as três variantes que aparecem no dado real (ASCII, aspa
    /// tipográfica e crase).
    /// </summary>
    private static bool IsIntraWordPunctuation(char character) => character is '\'' or '’' or '`';

    private static string KeepAlphanumericAndCollapseSpaces(string text)
    {
        var builder = new StringBuilder(text.Length);
        bool pendingSpace = false;

        foreach (char character in text)
        {
            if (IsIntraWordPunctuation(character))
                continue;

            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && builder.Length > 0)
                    builder.Append(' ');

                pendingSpace = false;
                builder.Append(character);
            }
            else
            {
                pendingSpace = true;
            }
        }

        return builder.ToString();
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
