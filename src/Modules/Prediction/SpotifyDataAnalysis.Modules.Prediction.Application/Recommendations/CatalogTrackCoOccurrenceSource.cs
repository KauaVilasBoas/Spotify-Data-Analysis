using System.Data;
using Dapper;
using SpotifyDataAnalysis.Infrastructure.Data;

namespace SpotifyDataAnalysis.Modules.Prediction.Application.Recommendations;

/// <summary>
/// Leitura da matriz de co-ocorrência pré-computada (E4.6) com Dapper, herdando <see cref="BaseDataAccess"/>. Lê a
/// tabela <c>prediction.track_cooccurrence</c> (materializada pelo passo batch, DP-3), NUNCA o self-join sobre o
/// jsonb das playlists a cada request — é o que torna o blend viável no volume do Pichl.
///
/// <para><b>Par canônico ordenado:</b> a tabela guarda cada aresta uma vez, com <c>track_id_low &lt; track_id_high</c>.
/// A semente pode ser qualquer um dos lados, então a consulta une os dois casos (semente = low → vizinho é high; e
/// vice-versa) e projeta o vizinho e o Jaccard nos dois. O <c>UNION ALL</c> não duplica: os dois ramos são
/// disjuntos por construção (um par nunca tem a semente nos dois lados).</para>
/// </summary>
internal sealed class CatalogTrackCoOccurrenceSource : BaseDataAccess, ITrackCoOccurrenceSource
{
    internal const string Sql =
        """
        SELECT neighbor_id AS "TrackId", co_playlists AS "CoPlaylists", jaccard AS "Jaccard"
        FROM (
            SELECT track_id_high AS neighbor_id, co_playlists, jaccard
            FROM prediction.track_cooccurrence
            WHERE track_id_low = @SeedTrackId
            UNION ALL
            SELECT track_id_low AS neighbor_id, co_playlists, jaccard
            FROM prediction.track_cooccurrence
            WHERE track_id_high = @SeedTrackId
        ) AS neighbors
        ORDER BY jaccard DESC, co_playlists DESC
        LIMIT @Limit;
        """;

    public CatalogTrackCoOccurrenceSource(DbConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public async Task<IReadOnlyList<CoOccurringTrack>> FindCoOccurringAsync(
        string seedTrackId, int limit, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(seedTrackId) || limit <= 0)
            return [];

        using IDbConnection connection = await OpenConnectionAsync();

        var command = new CommandDefinition(
            Sql, new { SeedTrackId = seedTrackId, Limit = limit }, cancellationToken: cancellationToken);

        return (await connection.QueryAsync<CoOccurringTrack>(command)).ToArray();
    }
}
