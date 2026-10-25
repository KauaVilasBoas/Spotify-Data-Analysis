using System.Globalization;
using SpotifyDataAnalysis.Modules.Prediction.Application.Recommendations;
using SpotifyDataAnalysis.Modules.Prediction.Contracts.Recommendations;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Recommendations;

/// <summary>
/// A orquestração do caso de uso público (E4.2/E4.3): distinção 404/422 da semente, hidratação de metadados, top-K
/// da explicação, gênero compartilhado, avisos de imputação nunca silenciosos, ordenação por score e o estágio de
/// gênero híbrido do E4.3 (boost por default, off relaxa para o cosine puro, filtro duro, fallback quando a semente
/// não tem gênero utilizável e contribuição do gênero na explicação). Usa um índice real (catálogo pequeno) e um
/// fake do metadata source — nem catálogo nem HTTP entram aqui.
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
        new("seed", Raw(0.80, 0.80, 0.80, 120.0, 0.10, 0.10, 0.10, 0.05, -8.0), Genre: null, false),
        new("near", Raw(0.79, 0.81, 0.78, 122.0, 0.11, 0.10, 0.10, 0.05, -8.2), Genre: null, false),
        new("mid",  Raw(0.55, 0.55, 0.55, 110.0, 0.40, 0.20, 0.15, 0.06, -12.0), Genre: null, false),
        new("far",  Raw(0.10, 0.10, 0.10, 60.0,  0.90, 0.80, 0.70, 0.60, -30.0), Genre: null, false),
        new("imp",  Raw(0.78, 0.82, 0.79, 121.0, 0.12, 0.11, 0.10, 0.05, -8.1), Genre: null, IsImputed: true)
    ];

    private static ITrackSimilarityIndexProvider IndexOf(IReadOnlyList<RawTrackFeatures> catalog) =>
        new StubIndexProvider(SimilarityIndex.Build(catalog));

    // O default do helper é dedupe=false: estes testes cobrem o RANKING (E4.2/E4.3), e o dedup do E4.7 é
    // pós-processamento com testes próprios. Ligá-lo aqui só adicionaria over-fetch sem mudar o que se afirma —
    // exceto onde um teste específico do dedup pede dedupe=true.
    private static GetTrackRecommendationsQuery Query(
        string seed, int limit = 10, int explainTopK = 3,
        GenreRankingModeContract genreMode = GetTrackRecommendationsQuery.DefaultGenreMode,
        bool dedupe = false,
        RecommendationStrategyContract strategy = RecommendationStrategyContract.Content,
        double blendWeight = GetTrackRecommendationsQuery.DefaultBlendWeight) =>
        new(seed, limit, explainTopK, genreMode, dedupe, strategy, blendWeight);

    private static TrackMetadataRow Meta(
        string id, string? name = null, string? artist = null, string? album = null,
        string? genre = null, int popularity = 50, bool exists = true, bool imputed = false, bool complete = true) =>
        new(id, name ?? $"Track {id}", artist ?? $"Artist {id}", album ?? $"Album {id}",
            genre, popularity, HasAudioFeatures: exists, IsImputed: imputed, HasCompleteFeatures: complete);

    [Fact]
    public async Task Handle_UnknownSeed_ThrowsNotFound()
    {
        var handler = Handler(
            IndexOf(SampleCatalog()), new StubMetadataSource());

        await Assert.ThrowsAsync<NotFoundException>(() => handler.HandleAsync(Query("ghost", limit: 5)));
    }

    [Fact]
    public async Task Handle_SeedExistsButHasNoFeatures_ThrowsBusiness()
    {
        // A semente "orphan" NÃO está no índice (sem features), mas existe no catálogo → 422, não 404.
        var metadata = new StubMetadataSource();
        metadata.Add(Meta("orphan", complete: false));

        var handler = Handler(IndexOf(SampleCatalog()), metadata);

        await Assert.ThrowsAsync<BusinessException>(() => handler.HandleAsync(Query("orphan", limit: 5)));
    }

    [Fact]
    public async Task Handle_KnownSeed_ReturnsNeighborsOrderedByScoreWithoutTheSeed()
    {
        var metadata = new StubMetadataSource();
        metadata.Add(Meta("seed"));
        foreach (string id in new[] { "near", "mid", "far", "imp" })
            metadata.Add(Meta(id));

        var handler = Handler(IndexOf(SampleCatalog()), metadata);

        TrackRecommendationsResponse response = await handler.HandleAsync(Query("seed"));

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

        var handler = Handler(IndexOf(SampleCatalog()), metadata);

        TrackRecommendationsResponse response = await handler.HandleAsync(Query("seed"));

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

        var handler = Handler(IndexOf(SampleCatalog()), metadata);

        TrackRecommendationsResponse response = await handler.HandleAsync(Query("seed"));

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
        // Modo OFF: o gênero não pesa, mas o sharedGenre descritivo (E4.2) ainda aparece quando os rótulos batem.
        var metadata = new StubMetadataSource();
        metadata.Add(Meta("seed", genre: "pop"));
        metadata.Add(Meta("near", genre: "pop"));   // igual → compartilhado
        metadata.Add(Meta("mid", genre: "rock"));   // diferente → nulo
        metadata.Add(Meta("far", genre: null));     // ausente → nulo
        metadata.Add(Meta("imp", genre: "pop"));

        var handler = Handler(IndexOf(SampleCatalog()), metadata);

        TrackRecommendationsResponse response =
            await handler.HandleAsync(Query("seed", genreMode: GenreRankingModeContract.Off));

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

        var handler = Handler(IndexOf(SampleCatalog()), metadata);

        TrackRecommendationsResponse response = await handler.HandleAsync(Query("seed"));

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

        var handler = Handler(IndexOf(SampleCatalog()), metadata);

        TrackRecommendationsResponse response = await handler.HandleAsync(Query("seed"));

        Assert.True(response.SeedIsImputed);
        Assert.Contains(response.Warnings, warning => warning.Contains("faixa-semente"));
    }

    [Fact]
    public async Task Handle_CleanCase_HasNoWarnings()
    {
        var metadata = new StubMetadataSource();
        foreach (string id in new[] { "seed", "near", "mid", "far" })
            metadata.Add(Meta(id, genre: "pop", imputed: false));

        // Catálogo sem faixa imputada, todos "pop" (semente tem gênero → sem fallback → sem aviso).
        IReadOnlyList<RawTrackFeatures> cleanCatalog =
        [
            new("seed", Raw(0.80, 0.80, 0.80, 120.0, 0.10, 0.10, 0.10, 0.05, -8.0), Genre: "pop", false),
            new("near", Raw(0.79, 0.81, 0.78, 122.0, 0.11, 0.10, 0.10, 0.05, -8.2), Genre: "pop", false),
            new("mid",  Raw(0.55, 0.55, 0.55, 110.0, 0.40, 0.20, 0.15, 0.06, -12.0), Genre: "pop", false),
            new("far",  Raw(0.10, 0.10, 0.10, 60.0,  0.90, 0.80, 0.70, 0.60, -30.0), Genre: "pop", false)
        ];

        var handler = Handler(IndexOf(cleanCatalog), metadata);

        TrackRecommendationsResponse response = await handler.HandleAsync(Query("seed"));

        Assert.Empty(response.Warnings);
    }

    // --- E4.3: o estágio de gênero no ranking ---

    /// <summary>
    /// Catálogo espalhado (12 faixas, 6 gêneros) para os z-scores NÃO saturarem: os cossenos no topo ficam
    /// separados por centésimos, como no catálogo real, de modo que o boost DEFAULT de fato reordena.
    /// Por cosseno puro o top-3 é [rockA, rockB, popA] — só um "pop"; o boost default traz popA/popB à frente.
    /// </summary>
    private static IReadOnlyList<RawTrackFeatures> GenreCatalog() =>
    [
        new("seed",  Raw(0.80, 0.80, 0.80, 120.0, 0.10, 0.10, 0.15, 0.05, -8.0), Genre: "pop", false),
        new("rockA", Raw(0.78, 0.83, 0.77, 124.0, 0.08, 0.10, 0.16, 0.05, -7.5), Genre: "rock", false),
        new("rockB", Raw(0.83, 0.77, 0.83, 116.0, 0.13, 0.10, 0.14, 0.05, -8.6), Genre: "rock", false),
        new("popA",  Raw(0.72, 0.74, 0.73, 113.0, 0.18, 0.13, 0.20, 0.07, -9.4), Genre: "pop", false),
        new("popB",  Raw(0.75, 0.72, 0.76, 127.0, 0.14, 0.12, 0.11, 0.06, -9.0), Genre: "pop", false),
        new("j1",    Raw(0.50, 0.55, 0.45, 100.0, 0.40, 0.30, 0.30, 0.10, -13.0), Genre: "jazz", false),
        new("j2",    Raw(0.45, 0.40, 0.50, 95.0,  0.55, 0.45, 0.25, 0.12, -15.0), Genre: "jazz", false),
        new("e1",    Raw(0.90, 0.95, 0.60, 130.0, 0.02, 0.05, 0.35, 0.04, -5.0), Genre: "edm", false),
        new("e2",    Raw(0.88, 0.90, 0.55, 140.0, 0.03, 0.08, 0.40, 0.05, -4.5), Genre: "edm", false),
        new("c1",    Raw(0.30, 0.20, 0.30, 80.0,  0.85, 0.60, 0.12, 0.04, -18.0), Genre: "classical", false),
        new("c2",    Raw(0.25, 0.15, 0.25, 75.0,  0.90, 0.75, 0.10, 0.03, -20.0), Genre: "classical", false),
        new("h1",    Raw(0.85, 0.65, 0.60, 95.0,  0.15, 0.05, 0.25, 0.30, -6.0), Genre: "hiphop", false)
    ];

    private static StubMetadataSource GenreMetadata()
    {
        var metadata = new StubMetadataSource();
        metadata.Add(Meta("seed", genre: "pop"));
        metadata.Add(Meta("rockA", genre: "rock"));
        metadata.Add(Meta("rockB", genre: "rock"));
        metadata.Add(Meta("popA", genre: "pop"));
        metadata.Add(Meta("popB", genre: "pop"));
        metadata.Add(Meta("j1", genre: "jazz"));
        metadata.Add(Meta("j2", genre: "jazz"));
        metadata.Add(Meta("e1", genre: "edm"));
        metadata.Add(Meta("e2", genre: "edm"));
        metadata.Add(Meta("c1", genre: "classical"));
        metadata.Add(Meta("c2", genre: "classical"));
        metadata.Add(Meta("h1", genre: "hiphop"));
        return metadata;
    }

    [Fact]
    public async Task Handle_DefaultBoost_LiftsSameGenreCoherenceOverOff_SameSeed()
    {
        // O critério de aceite central do E4.3: com gênero LIGADO (default boost), o top-N tem coerência de gênero
        // MAIOR que com gênero DESLIGADO, para a MESMA semente. No cosine puro o top-2 é [rockA, rockB] (0 pop);
        // com o boost default os "pop" sobem.
        var handler = Handler(IndexOf(GenreCatalog()), GenreMetadata());

        TrackRecommendationsResponse boosted =
            await handler.HandleAsync(Query("seed", limit: 2, genreMode: GenreRankingModeContract.Boost));
        TrackRecommendationsResponse cosineOnly =
            await handler.HandleAsync(Query("seed", limit: 2, genreMode: GenreRankingModeContract.Off));

        int boostedSameGenre = boosted.Recommendations.Count(r => r.SharedGenre == "pop");
        int cosineSameGenre = cosineOnly.Recommendations.Count(r => r.SharedGenre == "pop");

        Assert.Equal(0, cosineSameGenre);
        Assert.True(
            boostedSameGenre > cosineSameGenre,
            $"Boost deveria elevar a coerência de gênero no top-N (boost={boostedSameGenre}, off={cosineSameGenre}).");
    }

    [Fact]
    public async Task Handle_Off_MatchesPureCosineRankingOfE41()
    {
        // Relaxamento total: off cai no cosine puro. Um vizinho de OUTRO gênero (rockA, cosseno maior) lidera;
        // nenhum boost incide e o score coincide com o cosseno.
        var handler = Handler(IndexOf(GenreCatalog()), GenreMetadata());

        TrackRecommendationsResponse response =
            await handler.HandleAsync(Query("seed", genreMode: GenreRankingModeContract.Off));

        Assert.Equal(GenreRankingModeContract.Off, response.EffectiveGenreMode);
        Assert.All(response.Recommendations, r => Assert.Equal(0.0, r.GenreBoost));
        Assert.All(response.Recommendations, r => Assert.Equal(r.CosineScore, r.Score, 9));
        Assert.Equal("rockA", response.Recommendations[0].TrackId);
    }

    [Fact]
    public async Task Handle_Boost_ExposesGenreContributionInExplanation()
    {
        // A explicabilidade coerente: uma vizinha do mesmo gênero mostra genreBoost > 0 e score = cosine + boost.
        var handler = Handler(IndexOf(GenreCatalog()), GenreMetadata());

        TrackRecommendationsResponse response =
            await handler.HandleAsync(Query("seed", genreMode: GenreRankingModeContract.Boost));

        TrackRecommendationItem popA = response.Recommendations.Single(r => r.TrackId == "popA");
        Assert.True(popA.GenreBoost > 0);
        Assert.Equal(popA.CosineScore + popA.GenreBoost, popA.Score, 9);
        Assert.Equal("pop", popA.SharedGenre);

        TrackRecommendationItem rockA = response.Recommendations.Single(r => r.TrackId == "rockA");
        Assert.Equal(0.0, rockA.GenreBoost);
        Assert.Null(rockA.SharedGenre);
    }

    [Fact]
    public async Task Handle_SameGenreOnly_FiltersOutOtherGenres()
    {
        // Filtro duro: só faixas do mesmo gênero da semente entram no top-N.
        var handler = Handler(IndexOf(GenreCatalog()), GenreMetadata());

        TrackRecommendationsResponse response =
            await handler.HandleAsync(Query("seed", limit: 10, genreMode: GenreRankingModeContract.SameGenreOnly));

        Assert.Equal(GenreRankingModeContract.SameGenreOnly, response.EffectiveGenreMode);
        Assert.All(response.Recommendations, r => Assert.Equal("pop", r.SharedGenre));
        Assert.All(response.Recommendations, r => Assert.Equal("pop", r.Genre));
        Assert.Equal(2, response.Recommendations.Count); // popA e popB (a semente autoexclui)
    }

    [Fact]
    public async Task Handle_SeedWithoutGenre_FallsBackToCosineOnly_AndSignals()
    {
        // Semente sem gênero utilizável: mesmo pedindo boost, cai graciosamente no cosine puro e SINALIZA (nunca
        // filtra para vazio nem boosta em silêncio).
        IReadOnlyList<RawTrackFeatures> catalog =
        [
            new("seed",    Raw(0.80, 0.80, 0.80, 120.0, 0.10, 0.10, 0.10, 0.05, -8.0), Genre: null, false),
            new("popNear", Raw(0.70, 0.72, 0.71, 118.0, 0.18, 0.12, 0.12, 0.06, -9.0), Genre: "pop", false),
            new("rockClose",Raw(0.79, 0.81, 0.78, 121.0, 0.11, 0.10, 0.10, 0.05, -8.1), Genre: "rock", false)
        ];
        var metadata = new StubMetadataSource();
        metadata.Add(Meta("seed", genre: null));
        metadata.Add(Meta("popNear", genre: "pop"));
        metadata.Add(Meta("rockClose", genre: "rock"));

        var handler = Handler(IndexOf(catalog), metadata);

        TrackRecommendationsResponse response =
            await handler.HandleAsync(Query("seed", genreMode: GenreRankingModeContract.Boost));

        Assert.Equal(GenreRankingModeContract.Boost, response.RequestedGenreMode);
        Assert.Equal(GenreRankingModeContract.Off, response.EffectiveGenreMode);
        Assert.True(response.GenreFellBackToCosineOnly);
        Assert.All(response.Recommendations, r => Assert.Equal(0.0, r.GenreBoost));
        Assert.Equal(2, response.Recommendations.Count);
        Assert.Contains(response.Warnings, w => w.Contains("gênero"));
    }

    [Fact]
    public async Task Handle_ImputedSeedGenre_DoesNotBoost_AndSignalsFallback()
    {
        // Semente com gênero IMPUTADO (DP-F): não se boosta com base num rótulo estimado; cai no cosine puro e sinaliza.
        IReadOnlyList<RawTrackFeatures> catalog =
        [
            new("seed",    Raw(0.80, 0.80, 0.80, 120.0, 0.10, 0.10, 0.10, 0.05, -8.0), Genre: "pop", IsImputed: true),
            new("popNear", Raw(0.70, 0.72, 0.71, 118.0, 0.18, 0.12, 0.12, 0.06, -9.0), Genre: "pop", false),
            new("rockClose",Raw(0.79, 0.81, 0.78, 121.0, 0.11, 0.10, 0.10, 0.05, -8.1), Genre: "rock", false)
        ];
        var metadata = new StubMetadataSource();
        metadata.Add(Meta("seed", genre: "pop", imputed: true));
        metadata.Add(Meta("popNear", genre: "pop"));
        metadata.Add(Meta("rockClose", genre: "rock"));

        var handler = Handler(IndexOf(catalog), metadata);

        TrackRecommendationsResponse response =
            await handler.HandleAsync(Query("seed", genreMode: GenreRankingModeContract.Boost));

        Assert.True(response.GenreFellBackToCosineOnly);
        Assert.All(response.Recommendations, r => Assert.Equal(0.0, r.GenreBoost));
    }

    [Fact]
    public async Task Handle_ImputedCandidateGenre_DoesNotReceiveBoost()
    {
        // DP-F pelo lado da candidata: a faixa "pop" imputada NÃO recebe boost, mesmo coincidindo o rótulo.
        IReadOnlyList<RawTrackFeatures> catalog =
        [
            new("seed",     Raw(0.80, 0.80, 0.80, 120.0, 0.10, 0.10, 0.10, 0.05, -8.0), Genre: "pop", false),
            new("popMeasured", Raw(0.70, 0.72, 0.71, 118.0, 0.18, 0.12, 0.12, 0.06, -9.0), Genre: "pop", false),
            new("popImputed",  Raw(0.71, 0.73, 0.70, 117.0, 0.17, 0.13, 0.11, 0.07, -9.1), Genre: "pop", IsImputed: true)
        ];
        var metadata = new StubMetadataSource();
        metadata.Add(Meta("seed", genre: "pop"));
        metadata.Add(Meta("popMeasured", genre: "pop"));
        metadata.Add(Meta("popImputed", genre: "pop", imputed: true));

        var handler = Handler(IndexOf(catalog), metadata);

        TrackRecommendationsResponse response =
            await handler.HandleAsync(Query("seed", genreMode: GenreRankingModeContract.Boost));

        Assert.True(response.Recommendations.Single(r => r.TrackId == "popMeasured").GenreBoost > 0);
        Assert.Equal(0.0, response.Recommendations.Single(r => r.TrackId == "popImputed").GenreBoost);
    }

    // --- E4.7: o dedup de quase-duplicatas no top-N ---

    /// <summary>
    /// Catálogo com um par quase-idêntico ("hitA"/"hitB": mesma "artista|título", vetores idênticos → cosseno ≈ 1)
    /// além de duas faixas distintas. É o cenário do card: a mesma música com track_ids diferentes.
    /// </summary>
    private static IReadOnlyList<RawTrackFeatures> DuplicateCatalog() =>
    [
        new("seed", Raw(0.80, 0.80, 0.80, 120.0, 0.10, 0.10, 0.10, 0.05, -8.0), Genre: "pop", false),
        new("hitA", Raw(0.79, 0.81, 0.78, 122.0, 0.11, 0.10, 0.10, 0.05, -8.2), Genre: "pop", false),
        new("hitB", Raw(0.79, 0.81, 0.78, 122.0, 0.11, 0.10, 0.10, 0.05, -8.2), Genre: "pop", false),
        new("other", Raw(0.60, 0.62, 0.58, 110.0, 0.20, 0.15, 0.12, 0.06, -10.0), Genre: "pop", false),
        new("far",  Raw(0.10, 0.10, 0.10, 60.0,  0.90, 0.80, 0.70, 0.60, -30.0), Genre: "pop", false)
    ];

    private static StubMetadataSource DuplicateMetadata()
    {
        var metadata = new StubMetadataSource();
        metadata.Add(Meta("seed", genre: "pop"));
        // hitA e hitB são a MESMA música (mesmo nome+artista), popularidades diferentes.
        metadata.Add(Meta("hitA", name: "The Hit", artist: "The Band", genre: "pop", popularity: 30));
        metadata.Add(Meta("hitB", name: "The Hit", artist: "The Band", genre: "pop", popularity: 95));
        metadata.Add(Meta("other", name: "Other", artist: "Someone", genre: "pop", popularity: 50));
        metadata.Add(Meta("far", name: "Far", artist: "Distant", genre: "pop", popularity: 50));
        return metadata;
    }

    [Fact]
    public async Task Handle_Dedupe_CollapsesTheSameSong_AndKeepsLimitDistinct()
    {
        var handler = Handler(IndexOf(DuplicateCatalog()), DuplicateMetadata());

        // limit=2, dedupe on: sem dedup o top-2 seria [hitA, hitB] (a mesma música duas vezes). Com dedup, o par
        // colapsa e "other" preenche a 2ª vaga — 2 músicas DISTINTAS.
        TrackRecommendationsResponse response =
            await handler.HandleAsync(Query("seed", limit: 2, genreMode: GenreRankingModeContract.Off, dedupe: true));

        Assert.True(response.DedupeApplied);
        Assert.Equal(2, response.Recommendations.Count);

        string[] ids = response.Recommendations.Select(r => r.TrackId).ToArray();
        Assert.Contains("other", ids);
        // hitA e hitB nunca aparecem JUNTOS — no máximo o representante do par.
        Assert.False(ids.Contains("hitA") && ids.Contains("hitB"));
    }

    [Fact]
    public async Task Handle_Dedupe_PicksMostPopularRepresentative_AndSignalsCollapsedCount()
    {
        var handler = Handler(IndexOf(DuplicateCatalog()), DuplicateMetadata());

        TrackRecommendationsResponse response =
            await handler.HandleAsync(Query("seed", limit: 3, genreMode: GenreRankingModeContract.Off, dedupe: true));

        // O representante do par é a versão mais popular (hitB, popularity 95).
        TrackRecommendationItem representative =
            response.Recommendations.Single(r => r.TrackId is "hitA" or "hitB");
        Assert.Equal("hitB", representative.TrackId);
        Assert.Equal(1, representative.EquivalentVersionsCollapsed);
        Assert.Equal(1, response.TotalDuplicatesCollapsed);
    }

    [Fact]
    public async Task Handle_DedupeOff_ShowsBothVersions_Raw()
    {
        var handler = Handler(IndexOf(DuplicateCatalog()), DuplicateMetadata());

        TrackRecommendationsResponse response =
            await handler.HandleAsync(Query("seed", limit: 2, genreMode: GenreRankingModeContract.Off, dedupe: false));

        Assert.False(response.DedupeApplied);
        Assert.Equal(0, response.TotalDuplicatesCollapsed);
        string[] ids = response.Recommendations.Select(r => r.TrackId).ToArray();
        // Sem dedup, as duas versões da mesma música ocupam o top-2 (o comportamento cru do E4.1).
        Assert.Contains("hitA", ids);
        Assert.Contains("hitB", ids);
    }

    // --- E4.6: o blend com o sinal colaborativo ---

    [Fact]
    public async Task Handle_Blend_BringsInACollaborativeOnlyTrack_NotInTheContentTopN()
    {
        // "far" está longe no áudio (cairia no fim do content), mas co-ocorre forte com a semente. No blend, ela
        // sobe e é marcada como blended (tem áudio) — o sinal colaborativo puxou uma faixa que o áudio esconderia.
        var metadata = new StubMetadataSource();
        foreach (string id in new[] { "seed", "near", "mid", "far", "imp" })
            metadata.Add(Meta(id, genre: "pop"));

        var coOccurrence = new StubCoOccurrenceSource();
        coOccurrence.Add("seed", new CoOccurringTrack("far", CoPlaylists: 40, Jaccard: 0.9));

        var handler = Handler(IndexOf(SampleCatalog()), metadata, coOccurrence);

        TrackRecommendationsResponse response = await handler.HandleAsync(
            Query("seed", limit: 3, genreMode: GenreRankingModeContract.Off,
                strategy: RecommendationStrategyContract.Blend, blendWeight: 0.6));

        Assert.Equal(RecommendationStrategyContract.Blend, response.EffectiveStrategy);
        Assert.False(response.CollaborativeSignalUnavailable);

        TrackRecommendationItem far = response.Recommendations.Single(r => r.TrackId == "far");
        Assert.Equal("blended", far.Signal);
        Assert.Equal(40, far.CoPlaylists);
        Assert.Equal(0.9, far.CoOccurrenceScore);
        // Com peso alto e Jaccard 0,9, "far" deixou de ser a última — o colaborativo a promoveu.
        Assert.NotEqual("far", response.Recommendations[^1].TrackId);
    }

    [Fact]
    public async Task Handle_Blend_WithoutCoOccurrence_FallsBackToContent_AndSignals()
    {
        var metadata = new StubMetadataSource();
        foreach (string id in new[] { "seed", "near", "mid", "far", "imp" })
            metadata.Add(Meta(id, genre: "pop"));

        // Matriz vazia: a semente não tem co-ocorrência → cai para content e sinaliza.
        var handler = Handler(IndexOf(SampleCatalog()), metadata, new StubCoOccurrenceSource());

        TrackRecommendationsResponse response = await handler.HandleAsync(
            Query("seed", genreMode: GenreRankingModeContract.Off,
                strategy: RecommendationStrategyContract.Blend));

        Assert.Equal(RecommendationStrategyContract.Content, response.EffectiveStrategy);
        Assert.True(response.CollaborativeSignalUnavailable);
        Assert.Contains(response.Warnings, w => w.Contains("colaborativo"));
        Assert.All(response.Recommendations, r => Assert.Null(r.Signal));
    }

    [Fact]
    public async Task Handle_ContentStrategy_DoesNotConsultCoOccurrence()
    {
        var metadata = new StubMetadataSource();
        foreach (string id in new[] { "seed", "near", "mid", "far", "imp" })
            metadata.Add(Meta(id, genre: "pop"));

        // Mesmo com co-ocorrência disponível, strategy=content (default) não a usa: nenhum item traz signal.
        var coOccurrence = new StubCoOccurrenceSource();
        coOccurrence.Add("seed", new CoOccurringTrack("far", 40, 0.9));

        var handler = Handler(IndexOf(SampleCatalog()), metadata, coOccurrence);

        TrackRecommendationsResponse response = await handler.HandleAsync(
            Query("seed", genreMode: GenreRankingModeContract.Off,
                strategy: RecommendationStrategyContract.Content));

        Assert.Equal(RecommendationStrategyContract.Content, response.EffectiveStrategy);
        Assert.All(response.Recommendations, r => Assert.Null(r.Signal));
        Assert.All(response.Recommendations, r => Assert.Equal(0, r.CoPlaylists));
    }

    // --- E4.10: o boost de gênero entra na chave de ordenação do blend, e o score exposto é o que ordenou ---

    /// <summary>
    /// O par que inverte: <c>rockA</c> tem cosseno MAIOR que <c>popA</c>, mas só <c>popA</c> compartilha o gênero da
    /// semente, e o boost default (0,05) a coloca à frente no ranking content. Dando aos dois o MESMO Jaccard, o
    /// sinal colaborativo não desempata nada — a única coisa que pode separá-los no blend é o score de content. Se o
    /// blend ordenar pelo cosseno em vez do score híbrido, <c>rockA</c> volta à frente e a ordem entregue ao usuário
    /// deixa de ser a que o blend pretendia produzir.
    /// </summary>
    [Fact]
    public async Task Handle_Blend_KeepsTheGenreBoostInTheFinalOrderingKey()
    {
        var coOccurrence = new StubCoOccurrenceSource();
        coOccurrence.Add(
            "seed",
            new CoOccurringTrack("rockA", CoPlaylists: 10, Jaccard: 0.5),
            new CoOccurringTrack("popA", CoPlaylists: 10, Jaccard: 0.5));

        var handler = Handler(IndexOf(GenreCatalog()), GenreMetadata(), coOccurrence);

        TrackRecommendationsResponse content = await handler.HandleAsync(
            Query("seed", limit: 11, genreMode: GenreRankingModeContract.Boost));

        Assert.True(
            RankOf(content, "popA") < RankOf(content, "rockA"),
            $"Premissa do cenário: no content o boost já põe popA à frente de rockA.{Describe(content)}");

        TrackRecommendationsResponse blended = await handler.HandleAsync(
            Query("seed", limit: 11, genreMode: GenreRankingModeContract.Boost,
                strategy: RecommendationStrategyContract.Blend));

        Assert.True(
            RankOf(blended, "popA") < RankOf(blended, "rockA"),
            "O boost de gênero foi descartado na ordenação final do blend: com Jaccard idêntico, rockA só pode " +
            $"passar popA se a chave de ordenação tiver ignorado o boost.{Describe(blended)}");

        // E4.2 coerente (E4.10): popA exibe "gênero compartilhado" como porquê E o gênero de fato a promoveu acima
        // de rockA. Antes da correção o porquê aparecia descrevendo um cálculo sem efeito na ordem entregue.
        TrackRecommendationItem popA = blended.Recommendations.Single(item => item.TrackId == "popA");
        Assert.Equal("pop", popA.SharedGenre);
        Assert.True(popA.GenreBoost > 0);

        TrackRecommendationItem rockA = blended.Recommendations.Single(item => item.TrackId == "rockA");
        Assert.Null(rockA.SharedGenre);
        Assert.Equal(0.0, rockA.GenreBoost);
    }

    /// <summary>
    /// DP-2 do E4.10: um único score. O número exposto em <c>Score</c> tem de ser o MESMO que ordenou o ranking, logo
    /// a lista entregue é não-crescente nele. Ordenar por um número e mostrar outro é a causa raiz do bug, e este
    /// teste falha no instante em que os dois se separarem de novo.
    /// </summary>
    [Fact]
    public async Task Handle_Blend_ExposesTheSameScoreThatOrdered()
    {
        var metadata = new StubMetadataSource();
        foreach (string id in new[] { "seed", "near", "mid", "far", "imp" })
            metadata.Add(Meta(id, genre: "pop"));

        var coOccurrence = new StubCoOccurrenceSource();
        coOccurrence.Add("seed", new CoOccurringTrack("far", CoPlaylists: 40, Jaccard: 0.9));

        var handler = Handler(IndexOf(SampleCatalog()), metadata, coOccurrence);

        TrackRecommendationsResponse response = await handler.HandleAsync(
            Query("seed", limit: 4, genreMode: GenreRankingModeContract.Off,
                strategy: RecommendationStrategyContract.Blend, blendWeight: 0.6));

        for (int i = 1; i < response.Recommendations.Count; i++)
        {
            Assert.True(
                response.Recommendations[i - 1].Score >= response.Recommendations[i].Score,
                $"O score exposto não é o que ordenou o ranking do blend.{Describe(response)}");
        }
    }

    private static int RankOf(TrackRecommendationsResponse response, string trackId)
    {
        for (int i = 0; i < response.Recommendations.Count; i++)
        {
            if (response.Recommendations[i].TrackId == trackId)
                return i;
        }

        return int.MaxValue;
    }

    private static string Describe(TrackRecommendationsResponse response)
    {
        IEnumerable<string> lines = response.Recommendations.Select((item, rank) => string.Format(
            CultureInfo.InvariantCulture,
            "  #{0} {1,-6} score={2:0.0000} cosine={3:0.0000} boost={4:0.0000} jaccard={5:0.0000}",
            rank, item.TrackId, item.Score, item.CosineScore, item.GenreBoost, item.CoOccurrenceScore));

        return Environment.NewLine + string.Join(Environment.NewLine, lines);
    }

    // --- E4.9: over-fetch adaptativo ---

    /// <summary>
    /// A semente do card: um grupo de 25 quase-duplicatas ocupa TODA a janela da primeira rodada (30 candidatas), e o
    /// dedup do E4.7 as colapsa num único item. Com over-fetch FIXO o usuário recebe 6 recomendações para um
    /// <c>limit=10</c>; com o adaptativo, a janela dobra e as 15 faixas distintas que estavam fora da primeira janela
    /// completam o top-10.
    ///
    /// <para><b>É o cenário exato que o E4.9 mediu em escala</b> (679 de 1.895 sementes de grupos de 11+ recebendo
    /// menos de 10), reduzido ao menor catálogo que o reproduz de forma determinística.</para>
    /// </summary>
    [Fact]
    public async Task Handle_SeedWhoseFirstWindowCollapses_StillReturnsTheRequestedLimit()
    {
        List<RawTrackFeatures> catalog = CollapsingCatalog();

        var handler = Handler(IndexOf(catalog), CollapsingMetadata(catalog));

        TrackRecommendationsResponse response = await handler.HandleAsync(
            Query("seed", limit: 10, genreMode: GenreRankingModeContract.Off, dedupe: true));

        Assert.Equal(10, response.Recommendations.Count);

        // A lista tem de continuar SEM repetição: completar o top-N não pode ser "desligar o dedup na segunda
        // rodada", que trocaria lista curta por lista repetida.
        Assert.Equal(
            response.Recommendations.Count,
            response.Recommendations.Select(item => item.TrackId).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// O outro lado do mesmo cenário: quando o catálogo INTEIRO é uma única obra repetida, nenhuma rodada resolve.
    /// O handler devolve o único item honesto em vez de preencher com candidata pior (o risco registrado no card) e
    /// sem girar até varrer o catálogo.
    /// </summary>
    [Fact]
    public async Task Handle_PathologicalSeed_ReturnsWhatExistsInsteadOfPadding()
    {
        var catalog = new List<RawTrackFeatures> { DuplicateTrack("seed", 0) };
        for (int i = 0; i < 40; i++)
            catalog.Add(DuplicateTrack($"dup{i:00}", i + 1));

        var handler = Handler(IndexOf(catalog), CollapsingMetadata(catalog));

        TrackRecommendationsResponse response = await handler.HandleAsync(
            Query("seed", limit: 10, genreMode: GenreRankingModeContract.Off, dedupe: true));

        TrackRecommendationItem only = Assert.Single(response.Recommendations);
        Assert.Equal(39, only.EquivalentVersionsCollapsed);
        Assert.Equal(39, response.TotalDuplicatesCollapsed);
    }

    /// <summary>
    /// Catálogo que reproduz o funil do card: 25 versões da MESMA obra (cosseno ≈ 1 entre si, e mais parecidas com a
    /// semente que qualquer outra faixa) seguidas de 15 faixas distintas entre si. Com <c>limit=10</c> a primeira
    /// janela (30) pega as 25 irmãs e só 5 das distintas.
    /// </summary>
    private static List<RawTrackFeatures> CollapsingCatalog()
    {
        var catalog = new List<RawTrackFeatures> { DuplicateTrack("seed", 0) };

        for (int i = 0; i < 25; i++)
            catalog.Add(DuplicateTrack($"dup{i:00}", i + 1));

        for (int k = 0; k < 15; k++)
            catalog.Add(new RawTrackFeatures($"far{k:00}", DistinctVector(k), Genre: null, IsImputed: false));

        return catalog;
    }

    /// <summary>
    /// Uma versão da obra duplicada: o vetor da semente com uma perturbação de 1e-5 em cada feature, grande o
    /// bastante para os ids serem distintos e pequena o bastante para o cosseno ficar acima do limiar de 0,999 do
    /// colapsador.
    /// </summary>
    private static RawTrackFeatures DuplicateTrack(string trackId, int ordinal) =>
        new(
            trackId,
            Raw(0.80 + (ordinal * 1e-5), 0.80, 0.80, 120.0, 0.10, 0.10, 0.10, 0.05, -8.0),
            Genre: null,
            IsImputed: false);

    /// <summary>
    /// Vetores mutuamente distantes, gerados por uma rotação determinística das nove features. Precisam de duas
    /// propriedades ao mesmo tempo: cosseno ENTRE eles abaixo do limiar do dedup (senão colapsariam e o top-10 não
    /// existiria nem com janela dobrada) e cosseno com a semente abaixo do das irmãs (senão entrariam na primeira
    /// janela e o cenário não reproduziria o defeito).
    /// </summary>
    private static SimilarityFeatureVector DistinctVector(int k)
    {
        static double Cycle(int k, int multiplier) => ((k * multiplier) % 15) / 14.0;

        return Raw(
            0.05 + (0.90 * Cycle(k, 7)),
            0.05 + (0.90 * Cycle(k, 11)),
            0.05 + (0.90 * Cycle(k, 13)),
            60.0 + (120.0 * Cycle(k, 4)),
            0.05 + (0.90 * Cycle(k, 8)),
            0.05 + (0.90 * Cycle(k, 2)),
            0.05 + (0.90 * Cycle(k, 14)),
            0.02 + (0.50 * Cycle(k, 1)),
            -30.0 + (28.0 * Cycle(k, 13)));
    }

    /// <summary>
    /// Metadata do catálogo de colapso: toda faixa <c>dup*</c> compartilha "artista|título" (o segundo critério do
    /// dedup, em OU com o cosseno), e cada <c>far*</c> tem obra própria.
    /// </summary>
    private static StubMetadataSource CollapsingMetadata(IReadOnlyList<RawTrackFeatures> catalog)
    {
        var metadata = new StubMetadataSource();

        foreach (RawTrackFeatures track in catalog)
        {
            bool isDuplicate = !track.TrackId.StartsWith("far", StringComparison.Ordinal);

            metadata.Add(Meta(
                track.TrackId,
                name: isDuplicate ? "A Mesma Obra" : $"Obra {track.TrackId}",
                artist: isDuplicate ? "O Mesmo Artista" : $"Artista {track.TrackId}"));
        }

        return metadata;
    }

    // --- E4.11: over-fetch unificado em RecommendationOverFetch ---

    /// <summary>
    /// O handler passa exatamente <see cref="RecommendationOverFetch.MaximumCountFor"/> para a fonte colaborativa
    /// quando strategy=blend — a janela da ÚLTIMA rodada, porque a varredura é uma só e as rodadas leem prefixos
    /// dela (E4.9). Testado para dois valores de <c>limit</c>: um acima do piso (20) e um abaixo (1). É também o teto
    /// absoluto de candidatas que o handler admite pedir a uma fonte externa.
    /// </summary>
    [Theory]
    [InlineData(20)]
    [InlineData(1)]
    public async Task Handle_Blend_PassesOverFetchCountFor_ToCoOccurrenceSource(int limit)
    {
        var metadata = new StubMetadataSource();
        foreach (string id in new[] { "seed", "near", "mid", "far", "imp" })
            metadata.Add(Meta(id));

        var capturingSource = new CapturingCoOccurrenceSource();
        capturingSource.Add("seed", new CoOccurringTrack("far", CoPlaylists: 5, Jaccard: 0.5));

        var handler = Handler(IndexOf(SampleCatalog()), metadata, capturingSource);

        await handler.HandleAsync(Query("seed", limit: limit,
            genreMode: GenreRankingModeContract.Off,
            strategy: RecommendationStrategyContract.Blend));

        Assert.Equal(RecommendationOverFetch.MaximumCountFor(limit), capturingSource.LastLimit);
    }

    // --- E4.12: a janela da rodada corta o FUNIL INTEIRO, não só o lado de áudio ---

    /// <summary>
    /// O defeito: depois do E4.9 o handler passou a buscar a janela da ÚLTIMA rodada da fonte colaborativa (120
    /// candidatas para <c>limit=10</c>, contra 30 antes) e a entregar essa lista INTEIRA a todas as rodadas, enquanto
    /// só o lado de áudio era recortado pela janela. A rodada 1 do blend passou a disputar com 4× mais colaborativas
    /// do que antes do E4.9 — logo a promessa de que "a rodada 1 é bit a bit o comportamento de antes do E4.9" era
    /// falsa no blend.
    ///
    /// <para>O cenário isola o mecanismo: as 30 primeiras colaborativas são forasteiras (não estão no catálogo) com
    /// Jaccard calibrado para NÃO alcançar o top-10, e a 31ª — a primeira além da janela da rodada 1 — é exatamente a
    /// faixa que o ranking de conteúdo deixou em 11º lugar. Se o lado colaborativo vazar além da janela, essa 31ª ganha
    /// a parcela colaborativa, passa a 10ª e a composição do top-10 muda sem nenhuma rodada 2 ter ocorrido.</para>
    /// </summary>
    [Fact]
    public async Task Handle_Blend_FirstRoundIgnoresCollaborativeBeyondTheRoundWindow()
    {
        const int limit = 10;
        const double collaborativeWeight = GetTrackRecommendationsQuery.DefaultBlendWeight;
        const double contentWeight = 1.0 - collaborativeWeight;
        int firstWindow = RecommendationOverFetch.CountForRound(limit, round: 1);

        List<RawTrackFeatures> catalog = SpreadCatalog(40);
        StubMetadataSource metadata = SpreadMetadata(catalog);
        ITrackSimilarityIndexProvider index = IndexOf(catalog);

        // O ranking de conteúdo puro é a régua: quem está no top-10 e quem é o primeiro de fora.
        TrackRecommendationsResponse contentRanking = await Handler(index, metadata).HandleAsync(
            Query("seed", limit: catalog.Count - 1, genreMode: GenreRankingModeContract.Off));

        string[] contentTopN = contentRanking.Recommendations
            .Take(limit).Select(item => item.TrackId).ToArray();
        string firstOutsideTopN = contentRanking.Recommendations[limit].TrackId;

        // A parcela de content do blend é o min-max do score de conteúdo DENTRO da janela da rodada 1.
        double[] windowScores = contentRanking.Recommendations
            .Take(firstWindow).Select(item => item.Score).ToArray();
        double lowest = windowScores.Min();
        double spread = windowScores.Max() - lowest;
        double NormalizedContent(double score) => (score - lowest) / spread;

        double lastInTopN = contentWeight * NormalizedContent(windowScores[limit - 1]);
        double firstOutside = contentWeight * NormalizedContent(windowScores[limit]);

        // Jaccard que vale METADE do que a última do top-10 já vale: forte o bastante para promover a 11ª acima da
        // 10ª, fraco o bastante para uma forasteira sem sinal de áudio nenhum não alcançar o top-10.
        double jaccard = lastInTopN / (2.0 * collaborativeWeight);

        Assert.True(
            jaccard is > 0.0 and <= 1.0,
            $"Premissa do cenário: o Jaccard calibrado tem de ser um sinal válido. Calculado: {jaccard}.");
        Assert.True(
            collaborativeWeight * jaccard < lastInTopN,
            "Premissa do cenário: uma forasteira só-colaborativa com esse Jaccard fica FORA do top-10 " +
            $"({collaborativeWeight * jaccard} vs {lastInTopN}).");
        Assert.True(
            firstOutside + (collaborativeWeight * jaccard) > lastInTopN,
            "Premissa do cenário: se o lado colaborativo vazar, a 11ª do conteúdo passa a 10ª " +
            $"({firstOutside + (collaborativeWeight * jaccard)} vs {lastInTopN}).");

        var coOccurrence = new StubCoOccurrenceSource();
        coOccurrence.Add(
            "seed",
            Enumerable.Range(0, firstWindow)
                .Select(i => new CoOccurringTrack($"outsider{i:00}", CoPlaylists: 9, Jaccard: jaccard))
                // A 31ª candidata: mesmo Jaccard, menos playlists — a fonte ordena por jaccard DESC, co_playlists
                // DESC, então ela é legitimamente a última e cai fora da janela da rodada 1.
                .Append(new CoOccurringTrack(firstOutsideTopN, CoPlaylists: 1, Jaccard: jaccard))
                .ToArray());

        TrackRecommendationsResponse blended = await Handler(index, metadata, coOccurrence).HandleAsync(
            Query("seed", limit: limit, genreMode: GenreRankingModeContract.Off,
                strategy: RecommendationStrategyContract.Blend));

        Assert.Equal(
            contentTopN,
            blended.Recommendations.Select(item => item.TrackId).ToArray());
    }

    /// <summary>
    /// O outro lado do critério: alargar a rodada tem de alargar AS DUAS listas. Aqui o lado de áudio está esgotado
    /// (27 vizinhas, das quais 25 são a mesma obra) e é só o lado colaborativo que ainda tem candidatas distintas a
    /// oferecer — além da janela da rodada 1. Se a janela recortasse o lado colaborativo mas o teto do laço continuasse
    /// sendo o tamanho da varredura de áudio, a rodada nunca alcançaria essas candidatas e o usuário receberia 4
    /// recomendações para um <c>limit=10</c>.
    /// </summary>
    [Fact]
    public async Task Handle_Blend_WiderRoundWidensTheCollaborativeSideToo()
    {
        var catalog = new List<RawTrackFeatures> { DuplicateTrack("seed", 0) };
        for (int i = 0; i < 25; i++)
            catalog.Add(DuplicateTrack($"dup{i:00}", i + 1));
        for (int k = 0; k < 2; k++)
            catalog.Add(new RawTrackFeatures($"far{k:00}", DistinctVector(k), Genre: null, IsImputed: false));

        StubMetadataSource metadata = CollapsingMetadata(catalog);

        // 27 forasteiras da MESMA obra (colapsam em 1) e, depois delas, 13 obras distintas: as distintas só existem
        // além da janela da rodada 1 (30).
        var coOccurring = new List<CoOccurringTrack>();
        for (int i = 0; i < 27; i++)
        {
            coOccurring.Add(new CoOccurringTrack($"clone{i:00}", CoPlaylists: 20, Jaccard: 0.50));
            metadata.Add(Meta($"clone{i:00}", name: "A Obra Clonada", artist: "O Artista Clonado"));
        }

        for (int i = 0; i < 13; i++)
        {
            coOccurring.Add(new CoOccurringTrack($"solo{i:00}", CoPlaylists: 5, Jaccard: 0.40));
            metadata.Add(Meta($"solo{i:00}", name: $"Obra Solo {i:00}", artist: $"Artista Solo {i:00}"));
        }

        var coOccurrence = new StubCoOccurrenceSource();
        coOccurrence.Add("seed", coOccurring.ToArray());

        var handler = Handler(IndexOf(catalog), metadata, coOccurrence);

        TrackRecommendationsResponse response = await handler.HandleAsync(
            Query("seed", limit: 10, genreMode: GenreRankingModeContract.Off, dedupe: true,
                strategy: RecommendationStrategyContract.Blend));

        Assert.Equal(10, response.Recommendations.Count);
    }

    /// <summary>
    /// Catálogo de vetores mutuamente espalhados, gerado por uma rotação determinística módulo 41 (primo) das nove
    /// features: os cossenos com a semente ficam distintos e bem separados, o que dá um ranking de conteúdo estável
    /// sem quase-duplicata nenhuma.
    /// </summary>
    private static List<RawTrackFeatures> SpreadCatalog(int count)
    {
        var catalog = new List<RawTrackFeatures>
        {
            new("seed", Raw(0.80, 0.80, 0.80, 120.0, 0.10, 0.10, 0.10, 0.05, -8.0), Genre: null, IsImputed: false)
        };

        for (int k = 0; k < count; k++)
            catalog.Add(new RawTrackFeatures($"c{k:00}", SpreadVector(k), Genre: null, IsImputed: false));

        return catalog;
    }

    private static SimilarityFeatureVector SpreadVector(int k)
    {
        static double Cycle(int k, int multiplier) => ((k * multiplier) % 41) / 40.0;

        return Raw(
            0.05 + (0.90 * Cycle(k, 17)),
            0.05 + (0.90 * Cycle(k, 23)),
            0.05 + (0.90 * Cycle(k, 29)),
            60.0 + (120.0 * Cycle(k, 11)),
            0.05 + (0.90 * Cycle(k, 31)),
            0.05 + (0.90 * Cycle(k, 7)),
            0.05 + (0.90 * Cycle(k, 37)),
            0.02 + (0.50 * Cycle(k, 13)),
            -30.0 + (28.0 * Cycle(k, 19)));
    }

    /// <summary>Metadata do catálogo espalhado: cada faixa é obra própria, então o dedup não tem o que colapsar.</summary>
    private static StubMetadataSource SpreadMetadata(IReadOnlyList<RawTrackFeatures> catalog)
    {
        var metadata = new StubMetadataSource();

        foreach (RawTrackFeatures track in catalog)
            metadata.Add(Meta(track.TrackId, name: $"Obra {track.TrackId}", artist: $"Artista {track.TrackId}"));

        return metadata;
    }

    /// <summary>Handler com um sinal colaborativo VAZIO por default — os testes de content/E4.3/E4.7 não usam blend.</summary>
    private static GetTrackRecommendationsQueryHandler Handler(
        ITrackSimilarityIndexProvider index, ITrackMetadataSource metadata,
        ITrackCoOccurrenceSource? coOccurrence = null) =>
        new(index, metadata, coOccurrence ?? new StubCoOccurrenceSource());

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

    private sealed class StubCoOccurrenceSource : ITrackCoOccurrenceSource
    {
        private readonly Dictionary<string, List<CoOccurringTrack>> _bySeed = new(StringComparer.Ordinal);

        public void Add(string seed, params CoOccurringTrack[] neighbors) => _bySeed[seed] = neighbors.ToList();

        public Task<IReadOnlyList<CoOccurringTrack>> FindCoOccurringAsync(
            string seedTrackId, int limit, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<CoOccurringTrack> found = _bySeed.TryGetValue(seedTrackId, out List<CoOccurringTrack>? list)
                ? list.Take(limit).ToArray()
                : [];

            return Task.FromResult(found);
        }
    }

    private sealed class CapturingCoOccurrenceSource : ITrackCoOccurrenceSource
    {
        private readonly Dictionary<string, List<CoOccurringTrack>> _bySeed = new(StringComparer.Ordinal);

        public int LastLimit { get; private set; }

        public void Add(string seed, params CoOccurringTrack[] neighbors) => _bySeed[seed] = neighbors.ToList();

        public Task<IReadOnlyList<CoOccurringTrack>> FindCoOccurringAsync(
            string seedTrackId, int limit, CancellationToken cancellationToken = default)
        {
            LastLimit = limit;
            IReadOnlyList<CoOccurringTrack> found = _bySeed.TryGetValue(seedTrackId, out List<CoOccurringTrack>? list)
                ? list.Take(limit).ToArray()
                : [];

            return Task.FromResult(found);
        }
    }
}
