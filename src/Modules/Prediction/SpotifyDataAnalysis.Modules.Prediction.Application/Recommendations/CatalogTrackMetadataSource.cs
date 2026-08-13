using System.Data;
using Dapper;
using SpotifyDataAnalysis.Infrastructure.Data;

namespace SpotifyDataAnalysis.Modules.Prediction.Application.Recommendations;

/// <summary>
/// Leitura dos metadados de exibição das faixas de uma recomendação (E4.2) do schema <c>catalog</c> com Dapper,
/// herdando <see cref="BaseDataAccess"/>. Acoplamento por DADO idêntico ao do E3.1/E4.1: lê as tabelas do Catalog
/// por SQL, sem referenciar tipo .NET algum daquele módulo — posição guardada pelos ArchTests.
///
/// <para>Projeta o mesmo artista principal do read-side do Analytics (<c>artists -&gt; 0 -&gt;&gt; 'Name'</c>) e o
/// álbum por <c>LEFT JOIN catalog.albums</c> (um álbum referenciado mas não registrado deixa o nome nulo, sem
/// sumir a faixa). O gênero e a marca de imputação saem do jsonb <c>audio_features</c> (chaves PascalCase — o
/// snake_case do DbContext não alcança owned types em JSON). <c>HasCompleteFeatures</c> repete EXATAMENTE o
/// predicado de elegibilidade de <see cref="CatalogSimilarityFeatureSource"/>, para "está no índice" e "tem
/// features completas" nunca divergirem.</para>
/// </summary>
internal sealed class CatalogTrackMetadataSource : BaseDataAccess, ITrackMetadataSource
{
    /// <summary>
    /// Predicado das nove features contínuas presentes — a MESMA elegibilidade do índice
    /// (<see cref="CatalogSimilarityFeatureSource"/>). Mantido como fragmento reutilizado nos dois SELECTs para
    /// não haver duas cópias que possam divergir.
    /// </summary>
    private const string CompleteFeaturesPredicate =
        """
        (t.audio_features ->> 'Danceability')     IS NOT NULL
        AND (t.audio_features ->> 'Energy')           IS NOT NULL
        AND (t.audio_features ->> 'Valence')          IS NOT NULL
        AND (t.audio_features ->> 'Tempo')            IS NOT NULL
        AND (t.audio_features ->> 'Acousticness')     IS NOT NULL
        AND (t.audio_features ->> 'Instrumentalness') IS NOT NULL
        AND (t.audio_features ->> 'Liveness')         IS NOT NULL
        AND (t.audio_features ->> 'Speechiness')      IS NOT NULL
        AND (t.audio_features ->> 'Loudness')         IS NOT NULL
        """;

    /// <summary>
    /// Projeção comum: identidade, rótulos de UI (nome/artista/álbum/gênero) e os flags que decidem os caminhos de
    /// erro da semente. <c>{0}</c> recebe o filtro (por um id ou por muitos) — o resto é idêntico nos dois modos.
    /// </summary>
    internal static readonly string Sql =
        $$"""
        SELECT
            t.id                                                          AS "TrackId",
            t.name                                                        AS "Name",
            t.artists -> 0 ->> 'Name'                                     AS "Artist",
            al.name                                                       AS "Album",
            t.audio_features ->> 'Genre'                                  AS "Genre",
            t.popularity                                                  AS "Popularity",
            (t.audio_features IS NOT NULL)                                AS "HasAudioFeatures",
            COALESCE((t.audio_features ->> 'IsImputed')::boolean, false)  AS "IsImputed",
            ({{CompleteFeaturesPredicate}})                               AS "HasCompleteFeatures"
        FROM catalog.tracks AS t
        LEFT JOIN catalog.albums AS al ON al.id = t.album_id
        WHERE {0};
        """;

    private static readonly string SqlByTrackId = string.Format(Sql, "t.id = @TrackId");
    private static readonly string SqlByTrackIds = string.Format(Sql, "t.id = ANY(@TrackIds)");

    public CatalogTrackMetadataSource(DbConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    /// <inheritdoc />
    public async Task<TrackMetadataRow?> FindByTrackIdAsync(
        string trackId, CancellationToken cancellationToken = default)
    {
        using IDbConnection connection = await OpenConnectionAsync();

        var command = new CommandDefinition(
            SqlByTrackId, new { TrackId = trackId }, cancellationToken: cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<TrackMetadataRow>(command);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, TrackMetadataRow>> FindByTrackIdsAsync(
        IReadOnlyCollection<string> trackIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackIds);

        if (trackIds.Count == 0)
            return new Dictionary<string, TrackMetadataRow>(StringComparer.Ordinal);

        using IDbConnection connection = await OpenConnectionAsync();

        // ANY(@TrackIds) com um array parametrizado: uma leitura só, sem montar um IN (...) de tamanho variável
        // (que trocaria o plano do banco a cada N distinto). O Npgsql mapeia o array de string para text[].
        var command = new CommandDefinition(
            SqlByTrackIds, new { TrackIds = trackIds.ToArray() }, cancellationToken: cancellationToken);

        IEnumerable<TrackMetadataRow> rows = await connection.QueryAsync<TrackMetadataRow>(command);

        return rows.ToDictionary(row => row.TrackId, StringComparer.Ordinal);
    }
}
