using System.Globalization;
using System.Text;

namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Deduplication;

/// <summary>
/// A chave "artista|título" normalizada que o dedup do top-N (E4.7) usa para reconhecer que dois <c>track_id</c>s
/// distintos são a MESMA música. É uma <b>reimplementação intencional</b> da regra do <c>TrackMatchKey</c> do
/// Catalog — mesma normalização (corta sufixo editorial após " - ", remove parênteses/colchetes, tira acento,
/// baixa a caixa, descarta pontuação, colapsa espaços).
///
/// <para><b>Por que copiar a regra em vez de referenciar o VO do Catalog:</b> o Prediction NUNCA referencia tipos
/// .NET do Catalog (fronteira guardada pelos ArchTests) — ele lê o schema <c>catalog</c> por SQL. A chave
/// persistida <c>match_key</c> não serve: o catálogo foi semeado sem artista (E1.10), então ela é só-título e
/// colidiria em massa. Reconstruir a chave "artista|título" aqui, a partir do nome e do artista que o read-side já
/// projeta (<c>artists -&gt; 0 -&gt;&gt; 'Name'</c> + <c>name</c>), é a via que respeita a fronteira. A duplicação
/// da regra é o custo consciente de não acoplar os módulos por um VO compartilhado — e o dedup ainda tem o cosseno
/// como critério primário independente desta chave.</para>
/// </summary>
public readonly record struct RecommendationDuplicateKey
{
    private const string EditorialSuffixSeparator = " - ";

    private RecommendationDuplicateKey(string value) => Value = value;

    /// <summary>A chave no formato <c>"artista|titulo"</c>, já normalizada.</summary>
    public string Value { get; }

    /// <summary><see langword="true"/> quando não sobrou texto útil (artista e título vazios após normalizar).</summary>
    public bool IsEmpty => Value is null or "|";

    /// <summary>Monta a chave a partir do título e do artista principal, ambos como o read-side os projeta.</summary>
    public static RecommendationDuplicateKey From(string? trackName, string? primaryArtistName)
        => new($"{Normalize(primaryArtistName)}|{Normalize(trackName)}");

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
}
