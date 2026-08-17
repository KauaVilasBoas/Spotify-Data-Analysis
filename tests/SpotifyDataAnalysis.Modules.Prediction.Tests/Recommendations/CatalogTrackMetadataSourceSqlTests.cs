using SpotifyDataAnalysis.Modules.Prediction.Application.Recommendations;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Recommendations;

/// <summary>
/// Contrato do SQL de metadados da recomendação (E4.2). Sem Postgres vivo: afirma, sobre o texto do SQL, que a
/// leitura projeta os rótulos de UI certos, que "features completas" repete o predicado do índice, e que segue o
/// dialeto PostgreSQL do <see cref="BaseDataAccess"/>. A execução real fica para o smoke test com banco.
/// </summary>
public sealed class CatalogTrackMetadataSourceSqlTests
{
    private static readonly string Sql = CatalogTrackMetadataSource.Sql;

    [Fact]
    public void Sql_ProjectsDisplayMetadata()
    {
        Assert.Contains("t.name", Sql);
        Assert.Contains("t.artists -> 0 ->> 'Name'", Sql);       // artista principal
        Assert.Contains("al.name", Sql);                          // álbum via join
        Assert.Contains("t.audio_features ->> 'Genre'", Sql);     // gênero
    }

    [Fact]
    public void Sql_JoinsAlbumsWithLeftJoin_SoAnUnregisteredAlbumDoesNotDropTheTrack()
    {
        Assert.Contains("LEFT JOIN catalog.albums AS al ON al.id = t.album_id", Sql);
    }

    [Fact]
    public void Sql_ExposesSeedErrorSignals()
    {
        // Distinção 404/422: existência + features completas saem na projeção.
        Assert.Contains("(t.audio_features IS NOT NULL)", Sql);
        Assert.Contains("\"HasAudioFeatures\"", Sql);
        Assert.Contains("\"HasCompleteFeatures\"", Sql);
    }

    [Fact]
    public void Sql_CompleteFeaturesPredicate_MatchesTheIndexEligibility()
    {
        // "Tem features completas" tem de exigir EXATAMENTE as nove contínuas que o índice exige — senão "está no
        // índice" e "tem features" divergem e um 422 vira um vizinho fantasma (ou vice-versa).
        foreach (string feature in new[]
                 {
                     "Danceability", "Energy", "Valence", "Tempo", "Acousticness",
                     "Instrumentalness", "Liveness", "Speechiness", "Loudness"
                 })
        {
            Assert.Contains($"(t.audio_features ->> '{feature}')", Sql);
        }
    }

    [Fact]
    public void Sql_MarksImputedFromJsonb_DefaultingToFalse()
    {
        Assert.Contains("COALESCE((t.audio_features ->> 'IsImputed')::boolean, false)", Sql);
    }

    [Fact]
    public void Sql_FollowsPostgresConventions_NoSqlServerDialect()
    {
        Assert.DoesNotContain("NOLOCK", Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[", Sql);
        Assert.DoesNotContain("TOP ", Sql, StringComparison.OrdinalIgnoreCase);
    }
}
