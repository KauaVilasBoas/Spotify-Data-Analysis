using System.Text;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Seeding;

/// <summary>
/// O mapa de reconciliação do E4.5: <c>TrackMatchKey("artista|título") → track_id do catálogo</c>, reconstruído
/// em memória a partir do <c>dataset.csv</c> original (o Kaggle "Spotify Tracks Dataset").
///
/// <para><b>Por que reconstruir a chave em vez de ler o <c>match_key</c> do banco:</b> o E1.10 semeou
/// <c>catalog.tracks</c> SEM nome de artista (o CSV traz nome, não <c>SpotifyArtistId</c>, e fabricar id seria
/// inventar dado). O <c>match_key</c> persistido é portanto SÓ TÍTULO — casar por ele fundiria todas as faixas
/// homônimas de artistas diferentes no mesmo balde. O <c>dataset.csv</c> é o único lugar que ainda tem o par
/// <c>track_id → artista</c>, então é dele que a chave "artista|título" completa é remontada, sem tocar em
/// <c>catalog.tracks</c>.</para>
///
/// <para><b>Política de colisão (DP-2):</b> uma <see cref="TrackMatchKey"/> heurística pode apontar para mais de
/// um <c>track_id</c> (homônimos do mesmo artista, ou a mesma faixa repetida por gênero no dataset). A distinção
/// importa:</para>
/// <list type="bullet">
///   <item>o MESMO <c>track_id</c> repetido sob a mesma chave é inofensivo — é a faixa listada uma vez por
///   gênero — e não conta como ambiguidade;</item>
///   <item><c>track_id</c>s DISTINTOS sob a mesma chave são ambíguos: resolver por um deles arbitrariamente
///   injetaria pares de co-ocorrência espúrios, então a chave inteira é <b>descartada</b> — some do mapa e passa
///   a não casar nada. Perde-se um punhado de títulos, preserva-se a limpeza do sinal.</item>
/// </list>
/// </summary>
internal sealed class CatalogMatchKeyIndex
{
    private readonly IReadOnlyDictionary<string, string> _keyToTrackId;

    private CatalogMatchKeyIndex(IReadOnlyDictionary<string, string> keyToTrackId, int ambiguousKeysDiscarded)
    {
        _keyToTrackId = keyToTrackId;
        AmbiguousKeysDiscarded = ambiguousKeysDiscarded;
    }

    /// <summary>Quantas chaves distintas o mapa resolve para um único <c>track_id</c>.</summary>
    public int ResolvableKeys => _keyToTrackId.Count;

    /// <summary>Quantas chaves foram descartadas por apontar para <c>track_id</c>s distintos (ambíguas).</summary>
    public int AmbiguousKeysDiscarded { get; }

    /// <summary>
    /// Constrói o índice varrendo o <c>dataset.csv</c> uma vez. Para cada linha, monta a chave
    /// <c>"artista|título"</c> via <see cref="TrackMatchKey.FromArtistList"/> (o primeiro artista da lista
    /// separada por <c>;</c>) e a associa ao <c>track_id</c>. Chaves vazias são ignoradas; colisões entre
    /// <c>track_id</c>s distintos derrubam a chave (ver política de colisão).
    /// </summary>
    /// <param name="catalogDatasetCsvPath">Caminho do <c>dataset.csv</c> original (com a coluna <c>artists</c>).</param>
    public static async Task<CatalogMatchKeyIndex> BuildFromCatalogDatasetAsync(
        string catalogDatasetCsvPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(catalogDatasetCsvPath))
            throw new FileNotFoundException(
                $"dataset.csv (catálogo) não encontrado para reconciliação: {catalogDatasetCsvPath}",
                catalogDatasetCsvPath);

        // Acumula por chave o(s) track_id(s) distintos vistos, para só no fim decidir quais chaves são únicas.
        // HashSet por chave porque a mesma faixa aparece repetida por gênero (mesmo id) e isso NÃO é ambiguidade.
        var candidates = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        await foreach (KaggleCatalogRow row in
            KaggleCatalogCsvReader.ReadAsync(catalogDatasetCsvPath, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(row.TrackId))
                continue;

            if (!TryBuildKey(
                    () => TrackMatchKey.FromArtistList(
                        SanitizeUnicode(row.TrackName), SanitizeUnicode(row.Artists)),
                    out TrackMatchKey? key)
                || key!.IsEmpty)
            {
                continue;
            }

            if (!candidates.TryGetValue(key.Value, out HashSet<string>? trackIds))
            {
                trackIds = new HashSet<string>(StringComparer.Ordinal);
                candidates[key.Value] = trackIds;
            }

            trackIds.Add(row.TrackId.Trim());
        }

        var resolved = new Dictionary<string, string>(candidates.Count, StringComparer.Ordinal);
        int ambiguous = 0;

        foreach ((string key, HashSet<string> trackIds) in candidates)
        {
            if (trackIds.Count == 1)
                resolved[key] = trackIds.First();
            else
                ambiguous++;
        }

        return new CatalogMatchKeyIndex(resolved, ambiguous);
    }

    /// <summary>
    /// Resolve uma linha do Pichl (<c>trackname</c>+<c>artistname</c>) a um <c>track_id</c> do catálogo pela mesma
    /// chave <c>"artista|título"</c>, ou devolve <see langword="null"/> quando a faixa não casa (não está no
    /// catálogo, ou a chave era ambígua e foi descartada). O casamento é do lado do Pichl com
    /// <see cref="TrackMatchKey.From"/> — um único artista, não uma lista.
    /// </summary>
    public string? ResolveTrackId(string? trackName, string? artistName)
    {
        if (!TryBuildKey(
                () => TrackMatchKey.From(SanitizeUnicode(trackName), SanitizeUnicode(artistName)),
                out TrackMatchKey? key)
            || key!.IsEmpty)
        {
            return null;
        }

        return _keyToTrackId.TryGetValue(key.Value, out string? trackId) ? trackId : null;
    }

    /// <summary>
    /// Monta a chave protegendo contra o resto do lixo Unicode que o <see cref="SanitizeUnicode"/> não pega
    /// (não-caracteres como U+FFFF, que também fazem <see cref="string.Normalize(System.Text.NormalizationForm)"/>
    /// lançar). Uma linha que ainda assim quebre a normalização não casa — é descartada, não propaga a exceção.
    /// </summary>
    private static bool TryBuildKey(Func<TrackMatchKey> build, out TrackMatchKey? key)
    {
        try
        {
            key = build();
            return true;
        }
        catch (ArgumentException)
        {
            key = null;
            return false;
        }
    }

    /// <summary>
    /// Remove code points Unicode inválidos (surrogates soltos e não-caracteres) antes de a string tocar em
    /// <see cref="TrackMatchKey"/>. O CSV do Pichl vem de tweets — dado social real, com bytes malformados — e
    /// <see cref="string.Normalize(System.Text.NormalizationForm)"/> lança <see cref="ArgumentException"/> nesses
    /// pontos. Sanear AQUI (na fronteira de leitura do dado sujo), e não dentro do <see cref="TrackMatchKey"/>,
    /// mantém o VO de domínio intocado: uma linha com lixo Unicode simplesmente não casa, em vez de derrubar a
    /// carga inteira. Perder alguns caracteres de uma chave heurística é aceitável; abortar 12,9M linhas por uma
    /// não é.
    /// </summary>
    private static string? SanitizeUnicode(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var builder = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char current = text[i];

            if (char.IsHighSurrogate(current) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                builder.Append(current);
                builder.Append(text[i + 1]);
                i++;
                continue;
            }

            // Surrogate solto (alto sem baixo, ou baixo sem alto): inválido — descartado.
            if (char.IsSurrogate(current))
                continue;

            builder.Append(current);
        }

        return builder.ToString();
    }
}
