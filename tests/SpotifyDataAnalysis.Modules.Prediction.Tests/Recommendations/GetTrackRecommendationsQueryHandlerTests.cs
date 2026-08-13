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
}
