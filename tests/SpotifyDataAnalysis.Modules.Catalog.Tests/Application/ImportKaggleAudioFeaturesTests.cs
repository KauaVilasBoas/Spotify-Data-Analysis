using System.Runtime.CompilerServices;
using SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;
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
        string trackId, string? trackName = null, string? artists = null, string? genre = "rock")
        => new(trackId, trackName, artists, genre,
            0.8, 0.6, 0.5, 120, 0.1, 0.0, 0.2, 0.05, -5.0, 5, 1, 4);

    private static ImportKaggleAudioFeaturesCommandHandler Build(
        InMemoryTrackRepository tracks, params KaggleAudioFeaturesRow[] rows)
        => new(
            new FakeReader(rows),
            new TrackMatcher([
                new SpotifyTrackIdMatchingStrategy(tracks),
                new NameAndArtistMatchingStrategy(tracks)
            ]));

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
        Assert.Equal(0.75, parsed.Danceability);
        Assert.Equal(120.5, parsed.Tempo);
        Assert.Equal(4, parsed.TimeSignature);
    }
}
