using System.Runtime.CompilerServices;
using SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;
using SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Ingestion;

namespace SpotifyDataAnalysis.Modules.Catalog.Tests.Application;

/// <summary>
/// Testes da importação de audio-features do Kaggle: o handler casa por <c>track_id</c> e anexa as features
/// (com métrica de match), e o leitor CSV parseia por nome de coluna — ambos sem tocar em rede/banco.
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

    private sealed class InMemoryTrackRepository : ITrackRepository
    {
        private readonly Dictionary<string, Track> _store = new();

        public IReadOnlyDictionary<string, Track> Store => _store;

        public void Seed(Track track) => _store[track.Id.Value] = track;

        public Task<Track?> GetByIdAsync(SpotifyTrackId id, CancellationToken cancellationToken = default)
            => Task.FromResult(_store.TryGetValue(id.Value, out Track? track) ? track : null);

        public Task AddAsync(Track track, CancellationToken cancellationToken = default)
        {
            _store[track.Id.Value] = track;
            return Task.CompletedTask;
        }
    }

    private static KaggleAudioFeaturesRow Row(string trackId)
        => new(trackId, 0.8, 0.6, 0.5, 120, 0.1, 0.0, 0.2, 0.05, -5.0, 5, 1, 4);

    private static Track TrackWithId(string id)
        => Track.Register(SpotifyTrackId.Of(id), "Song", Popularity.Of(50), 1000, false, null, Array.Empty<string>());

    [Fact]
    public async Task Import_AttachesFeatures_ToMatchingTracks_AndReportsMatchRate()
    {
        var repo = new InMemoryTrackRepository();
        repo.Seed(TrackWithId("t1"));
        repo.Seed(TrackWithId("t2"));
        var handler = new ImportKaggleAudioFeaturesCommandHandler(
            new FakeReader(Row("t1"), Row("t2"), Row("t3-inexistente")), repo);

        ImportKaggleAudioFeaturesResult result =
            await handler.HandleAsync(new ImportKaggleAudioFeaturesCommand("dataset.csv"));

        Assert.Equal(2, result.Matched);
        Assert.Equal(1, result.Unmatched);
        Assert.Equal(3, result.Total);
        Assert.InRange(result.MatchRate, 0.66, 0.67); // 2/3

        Assert.NotNull(repo.Store["t1"].AudioFeatures);
        Assert.Equal(0.8, repo.Store["t1"].AudioFeatures!.Danceability);
        Assert.Equal("kaggle:spotify-tracks-dataset", repo.Store["t1"].AudioFeatures!.Source);
    }

    [Fact]
    public async Task Import_ProcessesEachTrackOnlyOnce_WhenTheDatasetRepeatsIt()
    {
        // O dataset Kaggle lista a mesma faixa uma vez por gênero — a primeira ocorrência vence.
        var repo = new InMemoryTrackRepository();
        repo.Seed(TrackWithId("t1"));
        var handler = new ImportKaggleAudioFeaturesCommandHandler(
            new FakeReader(Row("t1"), Row("t1"), Row("t1")), repo);

        ImportKaggleAudioFeaturesResult result =
            await handler.HandleAsync(new ImportKaggleAudioFeaturesCommand("dataset.csv"));

        Assert.Equal(1, result.Matched);
        Assert.Equal(2, result.Duplicates);
        Assert.Equal(0, result.Unmatched);
        Assert.Equal(3, result.Total);
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

        Assert.Single(rows);
        Assert.Equal("abc", rows[0].TrackId);
        Assert.Equal(0.75, rows[0].Danceability);
        Assert.Equal(120.5, rows[0].Tempo);
        Assert.Equal(4, rows[0].TimeSignature);
    }
}
