using SpotifyDataAnalysis.Modules.Prediction.Application.Recommendations;
using SpotifyDataAnalysis.Modules.Prediction.Contracts.Recommendations;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Recommendations;

/// <summary>
/// A orquestração do caso de uso público (E4.2): distinção 404/422 da semente, hidratação de metadados, top-K da
/// explicação, gênero compartilhado (só informação), avisos de imputação nunca silenciosos e ordenação por score.
/// Usa um índice real (catálogo pequeno) e um fake do metadata source — nem catálogo nem HTTP entram aqui.
/// </summary>
public sealed class GetTrackRecommendationsQueryHandlerTests
{
    private static SimilarityFeatureVector Raw(
        double danceability, double energy, double valence, double tempo,
        double acousticness, double instrumentalness, double liveness, double speechiness, double loudness) =>
        SimilarityFeatureVector.Create(
        [
            danceability, energy, valence, tempo, acousticness,
            instrumentalness, liveness, speechiness, loudness
        ]);

    private static IReadOnlyList<RawTrackFeatures> SampleCatalog() =>
    [
        new("seed", Raw(0.80, 0.80, 0.80, 120.0, 0.10, 0.10, 0.10, 0.05, -8.0), false),
        new("near", Raw(0.79, 0.81, 0.78, 122.0, 0.11, 0.10, 0.10, 0.05, -8.2), false),
        new("mid",  Raw(0.55, 0.55, 0.55, 110.0, 0.40, 0.20, 0.15, 0.06, -12.0), false),
        new("far",  Raw(0.10, 0.10, 0.10, 60.0,  0.90, 0.80, 0.70, 0.60, -30.0), false),
        new("imp",  Raw(0.78, 0.82, 0.79, 121.0, 0.12, 0.11, 0.10, 0.05, -8.1), IsImputed: true)
    ];

    private static ITrackSimilarityIndexProvider IndexOf(IReadOnlyList<RawTrackFeatures> catalog) =>
        new StubIndexProvider(SimilarityIndex.Build(catalog));

    private static TrackMetadataRow Meta(
        string id, string? name = null, string? artist = null, string? album = null,
        string? genre = null, bool exists = true, bool imputed = false, bool complete = true) =>
        new(id, name ?? $"Track {id}", artist ?? $"Artist {id}", album ?? $"Album {id}",
            genre, HasAudioFeatures: exists, IsImputed: imputed, HasCompleteFeatures: complete);

    [Fact]
    public async Task Handle_UnknownSeed_ThrowsNotFound()
    {
        var handler = new GetTrackRecommendationsQueryHandler(
            IndexOf(SampleCatalog()), new StubMetadataSource());

        await Assert.ThrowsAsync<NotFoundException>(() => handler.HandleAsync(
            new GetTrackRecommendationsQuery("ghost", Limit: 5, ExplainTopK: 3)));
    }

    [Fact]
    public async Task Handle_SeedExistsButHasNoFeatures_ThrowsBusiness()
    {
        // A semente "orphan" NÃO está no índice (sem features), mas existe no catálogo → 422, não 404.
        var metadata = new StubMetadataSource();
        metadata.Add(Meta("orphan", complete: false));

        var handler = new GetTrackRecommendationsQueryHandler(IndexOf(SampleCatalog()), metadata);

        await Assert.ThrowsAsync<BusinessException>(() => handler.HandleAsync(
            new GetTrackRecommendationsQuery("orphan", Limit: 5, ExplainTopK: 3)));
    }

    [Fact]
    public async Task Handle_KnownSeed_ReturnsNeighborsOrderedByScoreWithoutTheSeed()
    {
        var metadata = new StubMetadataSource();
        metadata.Add(Meta("seed"));
        foreach (string id in new[] { "near", "mid", "far", "imp" })
            metadata.Add(Meta(id));

        var handler = new GetTrackRecommendationsQueryHandler(IndexOf(SampleCatalog()), metadata);

        TrackRecommendationsResponse response = await handler.HandleAsync(
            new GetTrackRecommendationsQuery("seed", Limit: 10, ExplainTopK: 3));

        Assert.Equal("seed", response.SeedTrackId);
        Assert.DoesNotContain(response.Recommendations, item => item.TrackId == "seed");
        Assert.Equal(4, response.Recommendations.Count);
        Assert.Equal(5, response.IndexedTrackCount);

        for (int i = 1; i < response.Recommendations.Count; i++)
            Assert.True(response.Recommendations[i - 1].Score >= response.Recommendations[i].Score);
    }

    [Fact]
    public async Task Handle_HydratesRecommendationsWithCatalogMetadata()
    {
        var metadata = new StubMetadataSource();
        metadata.Add(Meta("seed", name: "Seed Song", genre: "pop"));
        metadata.Add(Meta("near", name: "Near Song", artist: "The Neighbors", album: "Proximity", genre: "pop"));
        foreach (string id in new[] { "mid", "far", "imp" })
            metadata.Add(Meta(id));

        var handler = new GetTrackRecommendationsQueryHandler(IndexOf(SampleCatalog()), metadata);

        TrackRecommendationsResponse response = await handler.HandleAsync(
            new GetTrackRecommendationsQuery("seed", Limit: 10, ExplainTopK: 3));

        Assert.Equal("Seed Song", response.SeedName);
        Assert.Equal("pop", response.SeedGenre);

        TrackRecommendationItem near = response.Recommendations.Single(item => item.TrackId == "near");
        Assert.Equal("Near Song", near.Name);
        Assert.Equal("The Neighbors", near.Artist);
        Assert.Equal("Proximity", near.Album);
    }

    [Fact]
    public async Task Handle_TopKLimitsTheExplanation_AndOrdersByContributionDescending()
    {
        var metadata = new StubMetadataSource();
        metadata.Add(Meta("seed"));
        foreach (string id in new[] { "near", "mid", "far", "imp" })
            metadata.Add(Meta(id));

        var handler = new GetTrackRecommendationsQueryHandler(IndexOf(SampleCatalog()), metadata);

        TrackRecommendationsResponse response = await handler.HandleAsync(
            new GetTrackRecommendationsQuery("seed", Limit: 10, ExplainTopK: 3));

        foreach (TrackRecommendationItem item in response.Recommendations)
        {
            Assert.Equal(3, item.TopFeatures.Count);
            for (int i = 1; i < item.TopFeatures.Count; i++)
                Assert.True(item.TopFeatures[i - 1].Contribution >= item.TopFeatures[i].Contribution);
        }
    }

    [Fact]
    public async Task Handle_SharedGenre_SetOnlyWhenSeedAndCandidateMatch()
    {
        var metadata = new StubMetadataSource();
        metadata.Add(Meta("seed", genre: "pop"));
        metadata.Add(Meta("near", genre: "pop"));   // igual → compartilhado
        metadata.Add(Meta("mid", genre: "rock"));   // diferente → nulo
        metadata.Add(Meta("far", genre: null));     // ausente → nulo
        metadata.Add(Meta("imp", genre: "pop"));

        var handler = new GetTrackRecommendationsQueryHandler(IndexOf(SampleCatalog()), metadata);

        TrackRecommendationsResponse response = await handler.HandleAsync(
            new GetTrackRecommendationsQuery("seed", Limit: 10, ExplainTopK: 3));

        Assert.Equal("pop", response.Recommendations.Single(i => i.TrackId == "near").SharedGenre);
        Assert.Null(response.Recommendations.Single(i => i.TrackId == "mid").SharedGenre);
        Assert.Null(response.Recommendations.Single(i => i.TrackId == "far").SharedGenre);
    }

    [Fact]
    public async Task Handle_ImputedRecommendation_IsFlaggedAndWarned()
    {
        var metadata = new StubMetadataSource();
        metadata.Add(Meta("seed", imputed: false));
        metadata.Add(Meta("near"));
        metadata.Add(Meta("mid"));
        metadata.Add(Meta("far"));
        metadata.Add(Meta("imp", imputed: true));

        var handler = new GetTrackRecommendationsQueryHandler(IndexOf(SampleCatalog()), metadata);

        TrackRecommendationsResponse response = await handler.HandleAsync(
            new GetTrackRecommendationsQuery("seed", Limit: 10, ExplainTopK: 3));

        Assert.False(response.SeedIsImputed);
        Assert.True(response.Recommendations.Single(i => i.TrackId == "imp").IsImputed);
        Assert.Contains(response.Warnings, warning => warning.Contains("IMPUTADAS"));
    }

    [Fact]
    public async Task Handle_ImputedSeed_WarnsAndFlagsSeed()
    {
        var metadata = new StubMetadataSource();
        metadata.Add(Meta("seed", imputed: true));
        foreach (string id in new[] { "near", "mid", "far", "imp" })
            metadata.Add(Meta(id));

        var handler = new GetTrackRecommendationsQueryHandler(IndexOf(SampleCatalog()), metadata);

        TrackRecommendationsResponse response = await handler.HandleAsync(
            new GetTrackRecommendationsQuery("seed", Limit: 10, ExplainTopK: 3));

        Assert.True(response.SeedIsImputed);
        Assert.Contains(response.Warnings, warning => warning.Contains("faixa-semente"));
    }

    [Fact]
    public async Task Handle_CleanCase_HasNoWarnings()
    {
        var metadata = new StubMetadataSource();
        // Sem a vizinha imputada no top: limita a 3 e o catálogo ordena "imp" logo após "near",
        // então excluímos "imp" trocando-o por metadados ausentes não resolve — usamos Limit alto e catálogo limpo.
        foreach (string id in new[] { "seed", "near", "mid", "far" })
            metadata.Add(Meta(id, imputed: false));

        // Catálogo sem faixa imputada.
        IReadOnlyList<RawTrackFeatures> cleanCatalog =
        [
            new("seed", Raw(0.80, 0.80, 0.80, 120.0, 0.10, 0.10, 0.10, 0.05, -8.0), false),
            new("near", Raw(0.79, 0.81, 0.78, 122.0, 0.11, 0.10, 0.10, 0.05, -8.2), false),
            new("mid",  Raw(0.55, 0.55, 0.55, 110.0, 0.40, 0.20, 0.15, 0.06, -12.0), false),
            new("far",  Raw(0.10, 0.10, 0.10, 60.0,  0.90, 0.80, 0.70, 0.60, -30.0), false)
        ];

        var handler = new GetTrackRecommendationsQueryHandler(IndexOf(cleanCatalog), metadata);

        TrackRecommendationsResponse response = await handler.HandleAsync(
            new GetTrackRecommendationsQuery("seed", Limit: 10, ExplainTopK: 3));

        Assert.Empty(response.Warnings);
    }

    private sealed class StubIndexProvider : ITrackSimilarityIndexProvider
    {
        private readonly SimilarityIndex _index;

        public StubIndexProvider(SimilarityIndex index) => _index = index;

        public Task<SimilarityIndex> GetIndexAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_index);
    }

    private sealed class StubMetadataSource : ITrackMetadataSource
    {
        private readonly Dictionary<string, TrackMetadataRow> _rows = new(StringComparer.Ordinal);

        public void Add(TrackMetadataRow row) => _rows[row.TrackId] = row;

        public Task<TrackMetadataRow?> FindByTrackIdAsync(
            string trackId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_rows.TryGetValue(trackId, out TrackMetadataRow? row) ? row : null);

        public Task<IReadOnlyDictionary<string, TrackMetadataRow>> FindByTrackIdsAsync(
            IReadOnlyCollection<string> trackIds, CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<string, TrackMetadataRow> found = trackIds
                .Where(_rows.ContainsKey)
                .ToDictionary(id => id, id => _rows[id], StringComparer.Ordinal);

            return Task.FromResult(found);
        }
    }
}
