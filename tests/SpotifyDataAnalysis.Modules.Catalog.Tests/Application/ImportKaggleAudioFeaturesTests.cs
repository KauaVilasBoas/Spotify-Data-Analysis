using System.Runtime.CompilerServices;
using SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;
using SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion.Imputation;
using SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion.Matching;
using SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Ingestion;
using SpotifyDataAnalysis.Modules.Catalog.Tests.Fakes;

namespace SpotifyDataAnalysis.Modules.Catalog.Tests.Application;

/// <summary>
/// Testes da importação de audio-features do Kaggle: o handler casa por <c>track_id</c> e, quando o id não
/// bate, pelo fallback nome+artista — reportando a métrica de match por estratégia. O leitor CSV parseia por
/// nome de coluna. Tudo sem tocar em rede/banco.
/// </summary>
public sealed class ImportKaggleAudioFeaturesTests
{
    private sealed class FakeReader : IKaggleAudioFeaturesReader
    {
        private readonly IReadOnlyList<KaggleAudioFeaturesRow> _rows;

        public FakeReader(params KaggleAudioFeaturesRow[] rows) => _rows = rows;

        public async IAsyncEnumerable<KaggleAudioFeaturesRow> ReadAsync(
            string csvFilePath, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            foreach (KaggleAudioFeaturesRow row in _rows)
                yield return row;
        }
    }

    private static KaggleAudioFeaturesRow Row(
        string trackId, string? trackName = null, string? artists = null, string? genre = "rock",
        int? durationMs = null)
        => new(trackId, trackName, artists, genre, durationMs,
            0.8, 0.6, 0.5, 120, 0.1, 0.0, 0.2, 0.05, -5.0, 5, 1, 4);

    // A chain de teste espelha a ORDEM registrada na DI (E1.9): track_id → nome+duração → nome+artista.
    private static ImportKaggleAudioFeaturesCommandHandler Build(
        InMemoryTrackRepository tracks, params KaggleAudioFeaturesRow[] rows)
        => new(
            new FakeReader(rows),
            new TrackMatcher([
                new SpotifyTrackIdMatchingStrategy(tracks),
                new NameAndDurationMatchingStrategy(tracks),
                new NameAndArtistMatchingStrategy(tracks)
            ]),
            new MedianAudioFeatureImputer());

    private static Task<ImportKaggleAudioFeaturesResult> ImportAsync(
        ImportKaggleAudioFeaturesCommandHandler handler)
        => handler.HandleAsync(new ImportKaggleAudioFeaturesCommand("dataset.csv"));

    [Fact]
    public async Task Import_AttachesFeatures_ToTracksMatchedById_AndReportsMatchRate()
    {
        var tracks = new InMemoryTrackRepository();
        tracks.Seed(CatalogFixtures.Track("t1"));
        tracks.Seed(CatalogFixtures.Track("t2"));

        ImportKaggleAudioFeaturesResult result = await ImportAsync(
            Build(tracks, Row("t1"), Row("t2"), Row("t3-inexistente")));

        Assert.Equal(2, result.MatchedById);
        Assert.Equal(0, result.MatchedByNameAndArtist);
        Assert.Equal(1, result.Unmatched);
        Assert.Equal(3, result.Total);
        Assert.InRange(result.MatchRate, 0.66, 0.67); // 2/3
        Assert.Equal(0d, result.FallbackRate);

        Assert.NotNull(tracks.Store["t1"].AudioFeatures);
        Assert.Equal(0.8, tracks.Store["t1"].AudioFeatures!.Danceability);
        Assert.Equal("rock", tracks.Store["t1"].AudioFeatures!.Genre);
        Assert.Equal("kaggle:spotify-tracks-dataset", tracks.Store["t1"].AudioFeatures!.Source);
    }

    [Fact]
    public async Task Import_FallsBackToNameAndArtist_WhenTheTrackIdDoesNotMatch()
    {
        var tracks = new InMemoryTrackRepository();
        tracks.Seed(CatalogFixtures.Track(
            "spotify-id", "Bohemian Rhapsody - Remastered 2011", artist: CatalogFixtures.Artist("a1", "Queen")));

        // O dataset traz outro id e o titulo "limpo" — so o fallback textual resolve.
        ImportKaggleAudioFeaturesResult result = await ImportAsync(
            Build(tracks, Row("kaggle-id", "Bohemian Rhapsody", "Queen")));

        Assert.Equal(0, result.MatchedById);
        Assert.Equal(1, result.MatchedByNameAndArtist);
        Assert.Equal(0, result.Unmatched);
        Assert.Equal(1d, result.FallbackRate);
        Assert.NotNull(tracks.Store["spotify-id"].AudioFeatures);
    }

    [Fact]
    public async Task Import_PrefersTheIdMatch_OverTheTextualFallback()
    {
        var tracks = new InMemoryTrackRepository();
        tracks.Seed(CatalogFixtures.Track("t1", "Song", artist: CatalogFixtures.Artist("a1", "Queen")));
        tracks.Seed(CatalogFixtures.Track("t2", "Outra", artist: CatalogFixtures.Artist("a1", "Queen")));

        ImportKaggleAudioFeaturesResult result = await ImportAsync(
            Build(tracks, Row("t1", "Song", "Queen")));

        Assert.Equal(1, result.MatchedById);
        Assert.Equal(0, result.MatchedByNameAndArtist);
    }

    [Fact]
    public async Task Import_IgnoresTheFallback_WhenThereIsNoUsableNameOrArtist()
    {
        var tracks = new InMemoryTrackRepository();
        tracks.Seed(CatalogFixtures.Track("t1"));

        ImportKaggleAudioFeaturesResult result = await ImportAsync(
            Build(tracks, Row("id-desconhecido", trackName: "   ", artists: null)));

        Assert.Equal(0, result.Matched);
        Assert.Equal(1, result.Unmatched);
    }

    [Fact]
    public async Task Import_ProcessesEachTrackOnlyOnce_WhenTheDatasetRepeatsIt()
    {
        // O dataset Kaggle lista a mesma faixa uma vez por genero — a primeira ocorrencia vence.
        var tracks = new InMemoryTrackRepository();
        tracks.Seed(CatalogFixtures.Track("t1"));

        ImportKaggleAudioFeaturesResult result = await ImportAsync(Build(
            tracks,
            Row("t1", genre: "rock"),
            Row("t1", genre: "pop"),
            Row("t1", genre: "metal")));

        Assert.Equal(1, result.Matched);
        Assert.Equal(2, result.Duplicates);
        Assert.Equal(0, result.Unmatched);
        Assert.Equal(3, result.Total);
        Assert.Equal("rock", tracks.Store["t1"].AudioFeatures!.Genre);
    }

    [Fact]
    public async Task Import_CountsAsDuplicate_WhenTwoRowsResolveToTheSameTrack()
    {
        var tracks = new InMemoryTrackRepository();
        tracks.Seed(CatalogFixtures.Track("t1", "Song", artist: CatalogFixtures.Artist("a1", "Queen")));

        ImportKaggleAudioFeaturesResult result = await ImportAsync(Build(
            tracks,
            Row("t1", "Song", "Queen"),
            Row("outro-id", "Song", "Queen")));

        Assert.Equal(1, result.MatchedById);
        Assert.Equal(0, result.MatchedByNameAndArtist);
        Assert.Equal(1, result.Duplicates);
    }

    [Fact]
    public async Task Import_FillsMissingValues_WithTheGenreMedian_AndFlagsThemAsImputed()
    {
        var tracks = new InMemoryTrackRepository();
        tracks.Seed(CatalogFixtures.Track("t1"));
        tracks.Seed(CatalogFixtures.Track("t2"));
        tracks.Seed(CatalogFixtures.Track("t3"));

        // Energias observadas no genero "rock": 0.2, 0.4 e 0.6 -> mediana 0.4. A faixa t3 nao tem energy.
        ImportKaggleAudioFeaturesResult result = await ImportAsync(Build(
            tracks,
            Row("t1") with { Energy = 0.2 },
            Row("t2") with { Energy = 0.6 },
            Row("t-fora-do-catalogo") with { Energy = 0.4 },
            Row("t3") with { Energy = null }));

        Assert.Equal(0.4, tracks.Store["t3"].AudioFeatures!.Energy, precision: 10);
        Assert.True(tracks.Store["t3"].AudioFeatures!.IsImputed);
        Assert.False(tracks.Store["t1"].AudioFeatures!.IsImputed);
        Assert.Equal(1, result.Imputed);
        Assert.InRange(result.ImputationRate, 0.33, 0.34); // 1 de 3 casadas
    }

    [Fact]
    public async Task Import_PrefersTheGenreMedian_OverTheGlobalOne()
    {
        var tracks = new InMemoryTrackRepository();
        tracks.Seed(CatalogFixtures.Track("rock1"));
        tracks.Seed(CatalogFixtures.Track("rock2"));
        tracks.Seed(CatalogFixtures.Track("bolero1"));
        tracks.Seed(CatalogFixtures.Track("bolero2"));
        tracks.Seed(CatalogFixtures.Track("alvo"));

        await ImportAsync(Build(
            tracks,
            Row("rock1", genre: "rock") with { Energy = 0.90 },
            Row("rock2", genre: "rock") with { Energy = 0.94 },
            Row("bolero1", genre: "bolero") with { Energy = 0.10 },
            Row("bolero2", genre: "bolero") with { Energy = 0.14 },
            // A mediana global seria ~0.52; a de bolero e 0.12.
            Row("alvo", genre: "bolero") with { Energy = null }));

        Assert.Equal(0.12, tracks.Store["alvo"].AudioFeatures!.Energy, precision: 10);
    }

    [Fact]
    public async Task Import_RoundsTheMedian_ForDiscreteFeatures()
    {
        var tracks = new InMemoryTrackRepository();
        tracks.Seed(CatalogFixtures.Track("t1"));
        tracks.Seed(CatalogFixtures.Track("t2"));
        tracks.Seed(CatalogFixtures.Track("t3"));

        // Medianas de "key": (4 + 5) / 2 = 4.5 -> 5 (nao existe tonalidade fracionaria).
        await ImportAsync(Build(
            tracks,
            Row("t1") with { Key = 4 },
            Row("t2") with { Key = 5 },
            Row("t3") with { Key = null }));

        Assert.Equal(5, tracks.Store["t3"].AudioFeatures!.Key);
    }

    [Fact]
    public async Task Import_FallsBackToZero_WhenTheFeatureIsMissingFromTheWholeDataset()
    {
        var tracks = new InMemoryTrackRepository();
        tracks.Seed(CatalogFixtures.Track("t1"));

        await ImportAsync(Build(tracks, Row("t1") with { Tempo = null }));

        Assert.Equal(0d, tracks.Store["t1"].AudioFeatures!.Tempo);
        Assert.True(tracks.Store["t1"].AudioFeatures!.IsImputed);
    }

    [Fact]
    public async Task Import_DisambiguatesHomonyms_ByDuration_WhenTheTrackIdDoesNotMatch()
    {
        // Duas gravações homônimas do mesmo artista colidem na TrackMatchKey ("queen|song"). A chave textual
        // pura casaria com a primeira arbitrariamente; a duração separa: o CSV traz 300s (dentro da
        // tolerancia de "longa", nao da "curta").
        var tracks = new InMemoryTrackRepository();
        tracks.Seed(CatalogFixtures.Track(
            "curta", "Song", artist: CatalogFixtures.Artist("a1", "Queen"), durationMs: 200_000));
        tracks.Seed(CatalogFixtures.Track(
            "longa", "Song", artist: CatalogFixtures.Artist("a1", "Queen"), durationMs: 300_000));

        ImportKaggleAudioFeaturesResult result = await ImportAsync(
            Build(tracks, Row("kaggle-id", "Song", "Queen", durationMs: 300_500)));

        Assert.Equal(1, result.MatchedByNameAndDuration);
        Assert.Equal(0, result.MatchedByNameAndArtist);
        Assert.Equal(0, result.MatchedById);
        // A features foi anexada a faixa CERTA (a longa), nao a primeira ocorrencia da chave.
        Assert.NotNull(tracks.Store["longa"].AudioFeatures);
        Assert.Null(tracks.Store["curta"].AudioFeatures);
        // O casamento por duracao NAO conta como fallback textual — a duracao confirma a faixa.
        Assert.Equal(0d, result.FallbackRate);
    }

    [Fact]
    public async Task Import_FollowsThePrecedenceOrder_TrackId_ThenDuration_ThenNameAndArtist()
    {
        var tracks = new InMemoryTrackRepository();
        // Alvo do match exato por id.
        tracks.Seed(CatalogFixtures.Track(
            "id-exato", "Song", artist: CatalogFixtures.Artist("a1", "Queen"), durationMs: 210_000));
        // Homônimas que só a duração separa.
        tracks.Seed(CatalogFixtures.Track(
            "dur-curta", "Ballad", artist: CatalogFixtures.Artist("a2", "Abba"), durationMs: 180_000));
        tracks.Seed(CatalogFixtures.Track(
            "dur-longa", "Ballad", artist: CatalogFixtures.Artist("a2", "Abba"), durationMs: 320_000));
        // Alvo do fallback textual puro (linha sem duração).
        tracks.Seed(CatalogFixtures.Track(
            "so-texto", "Anthem", artist: CatalogFixtures.Artist("a3", "Rush")));

        ImportKaggleAudioFeaturesResult result = await ImportAsync(Build(
            tracks,
            // Casa por id exato mesmo com duração divergente — o track_id vem primeiro.
            Row("id-exato", "Song", "Queen", durationMs: 999_000),
            // id inexistente + duração presente -> desambiguação por duração escolhe a longa.
            Row("kaggle-x", "Ballad", "Abba", durationMs: 319_000),
            // id inexistente + SEM duração -> a estratégia de duração cede a vez, cai no fallback textual.
            Row("kaggle-y", "Anthem", "Rush", durationMs: null)));

        Assert.Equal(1, result.MatchedById);
        Assert.Equal(1, result.MatchedByNameAndDuration);
        Assert.Equal(1, result.MatchedByNameAndArtist);
        Assert.NotNull(tracks.Store["id-exato"].AudioFeatures);
        Assert.NotNull(tracks.Store["dur-longa"].AudioFeatures);
        Assert.Null(tracks.Store["dur-curta"].AudioFeatures);
        Assert.NotNull(tracks.Store["so-texto"].AudioFeatures);
        Assert.InRange(result.FallbackRate, 0.33, 0.34); // 1 de 3 casamentos veio do fallback textual puro
    }

    [Fact]
    public async Task Import_YieldsToTheTextualFallback_WhenNoCandidateMatchesTheDurationWithinTolerance()
    {
        // Uma única faixa homônima, mas com duração muito distante da linha do CSV: a estratégia de duração
        // não encontra candidata na tolerância e CEDE A VEZ ao fallback textual puro, que então casa.
        var tracks = new InMemoryTrackRepository();
        tracks.Seed(CatalogFixtures.Track(
            "unica", "Song", artist: CatalogFixtures.Artist("a1", "Queen"), durationMs: 200_000));

        ImportKaggleAudioFeaturesResult result = await ImportAsync(
            Build(tracks, Row("kaggle-id", "Song", "Queen", durationMs: 260_000))); // 60s fora da tolerância

        Assert.Equal(0, result.MatchedByNameAndDuration);
        Assert.Equal(1, result.MatchedByNameAndArtist);
        Assert.Equal(1d, result.FallbackRate);
        Assert.NotNull(tracks.Store["unica"].AudioFeatures);
    }

    [Fact]
    public async Task Import_KeepsHomonymsApart_WhenNoDurationCandidateMatches()
    {
        // Duas homônimas, ambas longe da duração do CSV: sem candidata na tolerância, a estratégia de duração
        // cede a vez. O fallback textual puro assume e escolhe a primeira ocorrência determinística — mas o
        // ponto do teste é que a estratégia de duração NÃO inventou um casamento errado.
        var tracks = new InMemoryTrackRepository();
        tracks.Seed(CatalogFixtures.Track(
            "a", "Song", artist: CatalogFixtures.Artist("a1", "Queen"), durationMs: 100_000));
        tracks.Seed(CatalogFixtures.Track(
            "b", "Song", artist: CatalogFixtures.Artist("a1", "Queen"), durationMs: 400_000));

        ImportKaggleAudioFeaturesResult result = await ImportAsync(
            Build(tracks, Row("kaggle-id", "Song", "Queen", durationMs: 250_000)));

        Assert.Equal(0, result.MatchedByNameAndDuration);
        Assert.Equal(1, result.MatchedByNameAndArtist);
    }

    [Fact]
    public async Task Import_CountsAsMatchedByNameAndDuration_InTheMetric()
    {
        var tracks = new InMemoryTrackRepository();
        tracks.Seed(CatalogFixtures.Track(
            "t1", "Song", artist: CatalogFixtures.Artist("a1", "Queen"), durationMs: 200_000));

        ImportKaggleAudioFeaturesResult result = await ImportAsync(
            Build(tracks, Row("kaggle-id", "Song", "Queen", durationMs: 200_000)));

        Assert.Equal(1, result.Matched);
        Assert.Equal(1, result.MatchedByNameAndDuration);
        Assert.Equal(1d, result.MatchRate);
        Assert.Equal(0d, result.FallbackRate);
    }

    [Fact]
    public async Task CsvReader_ParsesRows_ByHeaderName()
    {
        const string csv =
            "track_id,artists,track_name,popularity,duration_ms,explicit,danceability,energy,key,loudness,mode,speechiness,acousticness,instrumentalness,liveness,valence,tempo,time_signature,track_genre\n" +
            "abc,Queen,Bohemian Rhapsody,82,354000,False,0.75,0.9,5,-3.2,1,0.04,0.12,0.0,0.2,0.55,120.5,4,rock\n";

        var rows = new List<KaggleAudioFeaturesRow>();
        await foreach (KaggleAudioFeaturesRow row in KaggleAudioFeaturesCsvReader.ParseAsync(new StringReader(csv)))
            rows.Add(row);

        KaggleAudioFeaturesRow parsed = Assert.Single(rows);
        Assert.Equal("abc", parsed.TrackId);
        Assert.Equal("Bohemian Rhapsody", parsed.TrackName);
        Assert.Equal("Queen", parsed.Artists);
        Assert.Equal("rock", parsed.Genre);
        Assert.Equal(354000, parsed.DurationMs);
        Assert.Equal(0.75, parsed.Danceability);
        Assert.Equal(120.5, parsed.Tempo);
        Assert.Equal(4, parsed.TimeSignature);
    }

    [Fact]
    public async Task CsvReader_ReadsEmptyCells_AsMissing_NotAsZero()
    {
        const string csv =
            "track_id,artists,track_name,danceability,energy,key,loudness,mode,speechiness,acousticness,instrumentalness,liveness,valence,tempo,time_signature,track_genre\n" +
            "abc,Queen,Bohemian Rhapsody,0.75,,5,-3.2,1,0.04,0.12,0.0,0.2,0.55,,4,rock\n";

        var rows = new List<KaggleAudioFeaturesRow>();
        await foreach (KaggleAudioFeaturesRow row in KaggleAudioFeaturesCsvReader.ParseAsync(new StringReader(csv)))
            rows.Add(row);

        KaggleAudioFeaturesRow parsed = Assert.Single(rows);
        Assert.Null(parsed.Energy);
        Assert.Null(parsed.Tempo);
        Assert.Equal(0.75, parsed.Danceability);
    }
}
