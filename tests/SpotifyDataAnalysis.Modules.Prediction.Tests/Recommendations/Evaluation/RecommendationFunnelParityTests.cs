using SpotifyDataAnalysis.Modules.Prediction.Application.Recommendations;
using SpotifyDataAnalysis.Modules.Prediction.Contracts.Recommendations;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Blending;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Deduplication;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Recommendations.Evaluation;

/// <summary>
/// Paridade do funil (E4.16): o handler de produção (<see cref="GetTrackRecommendationsQueryHandler"/>) e o avaliador
/// (<see cref="RecommenderQualityEvaluator.RankAsEndpointWould"/>) implementam o over-fetch, o dedup e o gênero DUAS
/// vezes. O gate de qualidade mede o funil do avaliador; se os dois divergirem, ele aprova um recomendador que o
/// endpoint não serve. Estes testes alimentam os dois com o mesmo índice sintético, as mesmas sementes e os mesmos
/// metadados e exigem o MESMO ranking (ids e ordem). Não há Postgres: índice e fontes são em memória.
/// </summary>
public sealed class RecommendationFunnelParityTests
{
    private const int Limit = 10;

    // Cenários de over-fetch: (irmãs da semente que colapsam em 1 obra, obras distintas, rodada que o dedup exige).
    // Janelas para limit=10: 30 / 60 / 120. A rodada exigida é VERIFICADA por sonda independente, não presumida.
    public static TheoryData<int, int, int> RoundScenarios => new()
    {
        { 3, 40, 1 },
        { 25, 15, 2 },
        { 70, 20, 3 }
    };

    // Todas as combinações de dedupe (ligado/desligado) e gênero (boost/off) em cada cenário de over-fetch.
    public static TheoryData<int, int, bool, bool> ParityCases()
    {
        var data = new TheoryData<int, int, bool, bool>();
        foreach ((int siblings, int distinct) in new[] { (3, 40), (25, 15), (70, 20) })
            foreach (bool dedupe in new[] { true, false })
                foreach (bool boost in new[] { true, false })
                    data.Add(siblings, distinct, dedupe, boost);

        return data;
    }

    [Theory]
    [MemberData(nameof(ParityCases))]
    public async Task Endpoint_e_avaliador_devolvem_os_mesmos_ids_na_mesma_ordem(
        int siblings, int distinct, bool dedupe, bool boost)
    {
        Fixture fixture = Scenario(siblings, distinct);

        string[] endpoint = await EndpointRanking(fixture, dedupe, boost);

        Assert.Equal(endpoint, EvaluatorRanking(fixture, dedupe, boost).Ids);
    }

    /// <summary>
    /// Verificação INDEPENDENTE da rodada: conta as obras distintas (por nome) nas janelas de 30/60/120 lidas direto
    /// do índice, sem passar pelo laço de over-fetch. A primeira janela com obras suficientes para o limite é a
    /// rodada que o cenário exige — se a fixture deixar de exigir 1, 2 e 3, é este teste que quebra.
    /// </summary>
    [Theory]
    [MemberData(nameof(RoundScenarios))]
    public void Fixture_exige_a_rodada_declarada_de_over_fetch(int siblings, int distinct, int expectedRounds)
    {
        Fixture fixture = Scenario(siblings, distinct);
        GenreAffinityPolicy policy = GenreAffinityPolicy.Create(
            GenreRankingMode.Boost, "pop", seedGenreIsImputed: false);

        int firstSufficientRound = 0;
        for (int round = 1; round <= RecommendationOverFetch.MaximumRounds && firstSufficientRound == 0; round++)
        {
            IReadOnlyList<TrackSimilarity> window = fixture.Index.FindNearestTo(
                "seed", RecommendationOverFetch.CountForRound(Limit, round), policy)!;

            int distinctWorks = window.Select(neighbor => fixture.NameOf[neighbor.TrackId]).Distinct().Count();
            if (distinctWorks >= Limit)
                firstSufficientRound = round;
        }

        Assert.Equal(expectedRounds, firstSufficientRound);
    }

    [Theory]
    [MemberData(nameof(RoundScenarios))]
    public void Avaliador_reporta_a_mesma_rodada_que_a_fixture_exige(int siblings, int distinct, int expectedRounds)
    {
        Assert.Equal(
            expectedRounds,
            EvaluatorRanking(Scenario(siblings, distinct), dedupe: true, boost: true).RoundsUsed);
    }

    /// <summary>Sem isto os testes de gênero seriam vacuamente verdes: off e boost têm de ordenar diferente.</summary>
    [Fact]
    public async Task Fixture_faz_o_boost_de_genero_reordenar_o_ranking_em_relacao_ao_off()
    {
        Fixture fixture = Scenario(siblings: 3, distinct: 40);

        string[] off = await EndpointRanking(fixture, dedupe: false, boost: false);
        string[] boost = await EndpointRanking(fixture, dedupe: false, boost: true);

        Assert.NotEqual(off, boost);
    }

    // --- blend (strategy=blend) ---

    private const double AlternativeBlendWeight = 0.8;

    // Todas as combinações de dedupe, gênero (boost/off) e peso do blend (default e outro) em cada cenário de over-fetch.
    public static TheoryData<int, int, bool, bool, double> BlendParityCases()
    {
        var data = new TheoryData<int, int, bool, bool, double>();
        foreach ((int siblings, int distinct) in new[] { (3, 40), (25, 15), (70, 20) })
            foreach (bool dedupe in DedupeSettingsFor(siblings))
                foreach (bool boost in new[] { true, false })
                    foreach (double weight in new[] { GetTrackRecommendationsQuery.DefaultBlendWeight, AlternativeBlendWeight })
                        data.Add(siblings, distinct, dedupe, boost, weight);

        return data;
    }

    [Theory]
    [MemberData(nameof(BlendParityCases))]
    public async Task Blend_endpoint_e_avaliador_devolvem_os_mesmos_ids_na_mesma_ordem(
        int siblings, int distinct, bool dedupe, bool boost, double weight)
    {
        Fixture fixture = BlendScenario(siblings, distinct);

        string[] endpoint = await EndpointRankingFor(
            fixture, dedupe, boost ? GenreRankingModeContract.Boost : GenreRankingModeContract.Off,
            RecommendationStrategyContract.Blend, weight);

        Assert.Equal(endpoint, EvaluatorRankingFor(fixture, Setting(dedupe, boost, weight)).Ids);
    }

    /// <summary>Sem isto a paridade do blend passaria com o blend sem efeito: no endpoint, blend e content têm de ordenar diferente.</summary>
    [Theory]
    [MemberData(nameof(BlendParityCases))]
    public async Task Fixture_faz_o_blend_ordenar_diferente_do_content_no_endpoint(
        int siblings, int distinct, bool dedupe, bool boost, double weight)
    {
        Fixture fixture = BlendScenario(siblings, distinct);
        GenreRankingModeContract mode = boost ? GenreRankingModeContract.Boost : GenreRankingModeContract.Off;

        string[] content = await EndpointRankingFor(
            fixture, dedupe, mode, RecommendationStrategyContract.Content, GetTrackRecommendationsQuery.DefaultBlendWeight);
        string[] blend = await EndpointRankingFor(fixture, dedupe, mode, RecommendationStrategyContract.Blend, weight);

        Assert.NotEqual(content, blend);
    }

    /// <summary>O mesmo do lado do avaliador: o blend dele também tem de mudar o ranking, senão a igualdade seria content = content.</summary>
    [Theory]
    [MemberData(nameof(BlendParityCases))]
    public void Fixture_faz_o_blend_ordenar_diferente_do_content_no_avaliador(
        int siblings, int distinct, bool dedupe, bool boost, double weight)
    {
        Fixture fixture = BlendScenario(siblings, distinct);

        string[] content = EvaluatorRankingFor(fixture, Setting(dedupe, boost, weight: null)).Ids;
        string[] blend = EvaluatorRankingFor(fixture, Setting(dedupe, boost, weight)).Ids;

        Assert.NotEqual(content, blend);
    }

    /// <summary>Sem isto a paridade com DOIS pesos seria a mesma prova duas vezes: o peso tem de mexer na ordem.</summary>
    [Theory]
    [InlineData(3, 40)]
    [InlineData(25, 15)]
    [InlineData(70, 20)]
    public async Task Fixture_faz_o_peso_do_blend_reordenar_o_ranking(int siblings, int distinct)
    {
        Fixture fixture = BlendScenario(siblings, distinct);

        string[] byDefault = await EndpointRankingFor(
            fixture, dedupe: false, GenreRankingModeContract.Off, RecommendationStrategyContract.Blend,
            GetTrackRecommendationsQuery.DefaultBlendWeight);
        string[] byAlternative = await EndpointRankingFor(
            fixture, dedupe: false, GenreRankingModeContract.Off, RecommendationStrategyContract.Blend,
            AlternativeBlendWeight);

        Assert.NotEqual(byDefault, byAlternative);
    }

    // --- só-colaborativa imputada e indexada ---

    [Fact]
    public async Task Blend_com_so_colaborativa_imputada_empatada_com_versao_medida_tem_a_mesma_paridade()
    {
        Fixture fixture = ImputedCollaborativeOnlyScenario();

        string[] endpoint = await EndpointRankingFor(
            fixture, dedupe: true, GenreRankingModeContract.Off, RecommendationStrategyContract.Blend,
            GetTrackRecommendationsQuery.DefaultBlendWeight);

        Assert.Equal(
            endpoint,
            EvaluatorRankingFor(fixture, Setting(dedupe: true, boost: false, GetTrackRecommendationsQuery.DefaultBlendWeight)).Ids);
    }

    /// <summary>Sem isto a paridade acima seria vacuamente verde: a imputada está indexada, fora da janela de áudio da rodada 1 e colapsa com a medida.</summary>
    [Fact]
    public async Task Fixture_faz_a_so_colaborativa_imputada_ficar_indexada_fora_da_janela_e_colapsar_com_a_medida()
    {
        Fixture fixture = ImputedCollaborativeOnlyScenario();
        GenreAffinityPolicy policy = GenreAffinityPolicy.Create(GenreRankingMode.Off, "pop", seedGenreIsImputed: false);

        IReadOnlyList<TrackSimilarity> window = fixture.Index.FindNearestTo(
            "seed", RecommendationOverFetch.CountForRound(Limit, 1), policy)!;

        Assert.True(fixture.Index.IsImputedTrack("collabImputed"));
        Assert.DoesNotContain(window, neighbor => neighbor.TrackId == "collabImputed");
        Assert.Equal(fixture.NameOf["collabImputed"], fixture.NameOf["far14"]);

        string[] withoutDedupe = await EndpointRankingFor(
            fixture, dedupe: false, GenreRankingModeContract.Off, RecommendationStrategyContract.Blend,
            GetTrackRecommendationsQuery.DefaultBlendWeight);
        Assert.Contains("collabImputed", withoutDedupe);
        Assert.Contains("far14", withoutDedupe);
    }

    // --- genreMode = SameGenreOnly (content) ---

    // Dedupe ligado/desligado em cada cenário de over-fetch.
    public static TheoryData<int, int, bool> SameGenreOnlyCases()
    {
        var data = new TheoryData<int, int, bool>();
        foreach ((int siblings, int distinct) in new[] { (3, 40), (25, 15), (70, 20) })
            foreach (bool dedupe in DedupeSettingsFor(siblings))
                data.Add(siblings, distinct, dedupe);

        return data;
    }

    // Sem dedupe, 10 ou mais irmãs ocupam o top-10 inteiro e nenhum blend ou filtro de gênero consegue mexer nele: o caso
    // seria verde por vacuidade (as guardas provaram). Sem dedupe só entra o cenário com poucas irmãs.
    private static bool[] DedupeSettingsFor(int siblings) => siblings < Limit ? [true, false] : [true];

    [Theory]
    [MemberData(nameof(SameGenreOnlyCases))]
    public async Task SameGenreOnly_endpoint_e_avaliador_devolvem_os_mesmos_ids_na_mesma_ordem(
        int siblings, int distinct, bool dedupe)
    {
        Fixture fixture = Scenario(siblings, distinct);

        string[] endpoint = await EndpointRankingFor(
            fixture, dedupe, GenreRankingModeContract.SameGenreOnly,
            RecommendationStrategyContract.Content, GetTrackRecommendationsQuery.DefaultBlendWeight);

        Assert.Equal(endpoint, EvaluatorRankingFor(fixture, SameGenreOnlySetting(dedupe)).Ids);
    }

    /// <summary>Guarda 1: sem candidato de outro gênero no ranking sem filtro, o filtro não tem o que remover.</summary>
    [Theory]
    [MemberData(nameof(SameGenreOnlyCases))]
    public async Task Fixture_tem_candidato_de_outro_genero_no_ranking_sem_filtro(
        int siblings, int distinct, bool dedupe)
    {
        Fixture fixture = Scenario(siblings, distinct);

        string[] unfiltered = await EndpointRankingFor(
            fixture, dedupe, GenreRankingModeContract.Off,
            RecommendationStrategyContract.Content, GetTrackRecommendationsQuery.DefaultBlendWeight);

        Assert.Contains(unfiltered, id => fixture.GenreOf[id] != "pop");
    }

    /// <summary>Guarda 2: o SameGenreOnly muda o ranking em relação ao off — remove candidatos de verdade.</summary>
    [Theory]
    [MemberData(nameof(SameGenreOnlyCases))]
    public async Task Fixture_faz_o_SameGenreOnly_remover_candidatos_em_relacao_ao_off(
        int siblings, int distinct, bool dedupe)
    {
        Fixture fixture = Scenario(siblings, distinct);

        string[] unfiltered = await EndpointRankingFor(
            fixture, dedupe, GenreRankingModeContract.Off,
            RecommendationStrategyContract.Content, GetTrackRecommendationsQuery.DefaultBlendWeight);
        string[] filtered = await EndpointRankingFor(
            fixture, dedupe, GenreRankingModeContract.SameGenreOnly,
            RecommendationStrategyContract.Content, GetTrackRecommendationsQuery.DefaultBlendWeight);

        Assert.NotEqual(unfiltered, filtered);
    }

    // --- fixture ---

    private static Task<string[]> EndpointRanking(Fixture fixture, bool dedupe, bool boost) =>
        EndpointRankingFor(
            fixture, dedupe, boost ? GenreRankingModeContract.Boost : GenreRankingModeContract.Off,
            RecommendationStrategyContract.Content, GetTrackRecommendationsQuery.DefaultBlendWeight);

    private static async Task<string[]> EndpointRankingFor(
        Fixture fixture, bool dedupe, GenreRankingModeContract mode,
        RecommendationStrategyContract strategy, double blendWeight)
    {
        var handler = new GetTrackRecommendationsQueryHandler(
            new StubIndexProvider(fixture.Index), new StubMetadataSource(fixture.Rows),
            new StubCoOccurrenceSource(fixture.CoOccurring));

        TrackRecommendationsResponse response = await handler.HandleAsync(new GetTrackRecommendationsQuery(
            "seed", Limit, ExplainTopK: 3, mode, dedupe, strategy, blendWeight));

        return response.Recommendations.Select(item => item.TrackId).ToArray();
    }

    private static RecommenderEvaluationSetting Setting(bool dedupe, bool boost, double? weight)
    {
        RecommenderEvaluationSetting setting = (boost
                ? RecommenderEvaluationSetting.BoostedBy(GenreAffinityPolicy.DefaultBoostWeight)
                : RecommenderEvaluationSetting.CosineOnly())
            .WithDedupe(dedupe);

        return weight is double blendWeight ? setting.WithBlend(blendWeight) : setting;
    }

    private static RecommenderEvaluationSetting SameGenreOnlySetting(bool dedupe) =>
        RecommenderEvaluationSetting.SameGenreOnly().WithDedupe(dedupe);

    private static EvaluatorResult EvaluatorRanking(Fixture fixture, bool dedupe, bool boost) =>
        EvaluatorRankingFor(fixture, Setting(dedupe, boost, weight: null));

    private static EvaluatorResult EvaluatorRankingFor(Fixture fixture, RecommenderEvaluationSetting setting)
    {
        var attributes = fixture.Rows.ToDictionary(
            row => row.TrackId,
            row => new EvaluationTrackAttributes(RecommendationDuplicateKey.From(row.Name, row.Artist), row.Popularity),
            StringComparer.Ordinal);

        var collaborative = new Dictionary<string, IReadOnlyList<BlendCollaborativeCandidate>>(StringComparer.Ordinal)
        {
            ["seed"] = fixture.CoOccurring
                .Select(track => new BlendCollaborativeCandidate(track.TrackId, track.CoPlaylists, track.Jaccard))
                .ToArray()
        };

        EndpointTopN top = RecommenderQualityEvaluator.RankAsEndpointWould(
            fixture.Index, "seed", Limit, setting, new RecommenderEvaluationContext(attributes, collaborative))!;

        return new EvaluatorResult(top.Neighbors.Select(neighbor => neighbor.TrackId).ToArray(), top.RoundsUsed);
    }

    private sealed record EvaluatorResult(string[] Ids, int RoundsUsed);

    private sealed record Fixture(
        SimilarityIndex Index, IReadOnlyList<RawTrackFeatures> Catalog, IReadOnlyList<TrackMetadataRow> Rows,
        IReadOnlyDictionary<string, string> NameOf,
        IReadOnlyList<CoOccurringTrack>? Collaborative = null)
    {
        public IReadOnlyList<CoOccurringTrack> CoOccurring => Collaborative ?? [];

        public IReadOnlyDictionary<string, string> GenreOf =>
            Rows.ToDictionary(row => row.TrackId, row => row.Genre!, StringComparer.Ordinal);
    }

    /// <summary>
    /// O cenário de <see cref="Scenario"/> mais sinal colaborativo DE VERDADE: faixas distantes no áudio (que o content
    /// jamais poria no topo), uma irmã e um gêmeo recebem Jaccard alto, e "collabOnly" só existe no colaborativo (está
    /// no catálogo, fora do índice de áudio). Ordenado por Jaccard decrescente, como a fonte real devolve.
    /// </summary>
    private static Fixture BlendScenario(int siblings, int distinct)
    {
        Fixture content = Scenario(siblings, distinct);

        List<TrackMetadataRow> rows =
            [.. content.Rows, Row("collabOnly", "Obra collabOnly", "Artista collabOnly", "pop", 90)];
        string[] ids = ["collabOnly", "far14", "far13", "far12", "far11", "far10", "dup00", "twinPop"];
        double[] jaccards = [0.90, 0.80, 0.70, 0.60, 0.50, 0.40, 0.45, 0.30];

        CoOccurringTrack[] collaborative = ids
            .Zip(jaccards, (id, jaccard) => new CoOccurringTrack(id, (int)(jaccard * 100), jaccard))
            .OrderByDescending(track => track.Jaccard)
            .ToArray();

        return new Fixture(
            content.Index, content.Catalog, rows,
            rows.ToDictionary(row => row.TrackId, row => row.Name!, StringComparer.Ordinal),
            collaborative);
    }

    /// <summary>
    /// O <see cref="BlendScenario"/> (3, 40) mais "collabImputed": indexada com features IMPUTADAS e a mais distante da
    /// semente no áudio, então só entra no blend pelo lado colaborativo (sem vizinha rica), com a mesma chave
    /// "artista|título" de "far14" (medida) e Jaccard maior — as duas empatam no dedup e só a marca de imputação decide.
    /// </summary>
    private static Fixture ImputedCollaborativeOnlyScenario()
    {
        Fixture blend = BlendScenario(siblings: 3, distinct: 40);

        var catalog = new List<RawTrackFeatures>(blend.Catalog)
        {
            new("collabImputed", Raw(0.01, 0.99, 0.01, 60.0, 0.99, 0.99, 0.99, 0.50, -30.0), "pop", IsImputed: true)
        };
        List<TrackMetadataRow> rows =
        [
            .. blend.Rows,
            Row("collabImputed", "Obra far14", "Artista far14", "pop", 34) with { IsImputed = true }
        ];
        CoOccurringTrack[] collaborative =
            [new("collabImputed", 95, 0.95), .. blend.CoOccurring];

        return new Fixture(
            SimilarityIndex.Build(catalog), catalog, rows,
            rows.ToDictionary(row => row.TrackId, row => row.Name!, StringComparer.Ordinal), collaborative);
    }

    /// <summary>
    /// A semente, <paramref name="siblings"/> versões da MESMA obra (mais parecidas com a semente que qualquer outra
    /// faixa e colapsáveis entre si) e <paramref name="distinct"/> obras distintas e espalhadas, de gêneros
    /// alternados — as irmãs partilham o gênero "pop" da semente. Popularidade determinística por faixa.
    /// </summary>
    private static Fixture Scenario(int siblings, int distinct)
    {
        var catalog = new List<RawTrackFeatures>
        {
            new("seed", Raw(0.80, 0.80, 0.80, 120.0, 0.10, 0.10, 0.10, 0.05, -8.0), "pop", IsImputed: false)
        };
        var rows = new List<TrackMetadataRow> { Row("seed", "Obra seed", "Artista seed", "pop", 50) };

        for (int i = 0; i < siblings; i++)
        {
            string id = $"dup{i:00}";
            catalog.Add(new RawTrackFeatures(
                id, Raw(0.80 + ((i + 1) * 1e-5), 0.80, 0.80, 120.0, 0.10, 0.10, 0.10, 0.05, -8.0), "pop", false));
            rows.Add(Row(id, "A Mesma Obra", "O Mesmo Artista", "pop", 20 + ((i * 7) % 60)));
        }

        // Dois gêmeos quase-colados à semente (cosseno ~0,99, abaixo do limiar do dedup) separados por menos que o
        // peso do boost: o de rock é o mais parecido, o de pop só passa à frente quando o gênero pesa.
        catalog.Add(new RawTrackFeatures(
            "twinRock", Raw(0.80, 0.80, 0.70, 120.0, 0.10, 0.10, 0.10, 0.05, -8.0), "rock", false));
        rows.Add(Row("twinRock", "Obra twinRock", "Artista twinRock", "rock", 40));
        catalog.Add(new RawTrackFeatures(
            "twinPop", Raw(0.80, 0.80, 0.66, 120.0, 0.10, 0.10, 0.10, 0.05, -8.0), "pop", false));
        rows.Add(Row("twinPop", "Obra twinPop", "Artista twinPop", "pop", 40));

        for (int k = 0; k < distinct; k++)
        {
            string id = $"far{k:00}";
            string genre = k % 2 == 0 ? "pop" : "rock";
            catalog.Add(new RawTrackFeatures(id, SpreadVector(k), genre, IsImputed: false));
            rows.Add(Row(id, $"Obra {id}", $"Artista {id}", genre, 30 + ((k * 11) % 50)));
        }

        return new Fixture(
            SimilarityIndex.Build(catalog), catalog, rows,
            rows.ToDictionary(row => row.TrackId, row => row.Name!, StringComparer.Ordinal));
    }

    private static TrackMetadataRow Row(string id, string name, string artist, string genre, int popularity) =>
        new(id, name, artist, $"Album {id}", genre, popularity,
            HasAudioFeatures: true, IsImputed: false, HasCompleteFeatures: true);

    private static SimilarityFeatureVector Raw(
        double danceability, double energy, double valence, double tempo,
        double acousticness, double instrumentalness, double liveness, double speechiness, double loudness) =>
        SimilarityFeatureVector.Create(
        [
            danceability, energy, valence, tempo, acousticness,
            instrumentalness, liveness, speechiness, loudness
        ]);

    /// <summary>Rotação determinística módulo 41 (primo) das nove features: vetores espalhados, sem quase-duplicata.</summary>
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

    private sealed class StubIndexProvider(SimilarityIndex index) : ITrackSimilarityIndexProvider
    {
        public Task<SimilarityIndex> GetIndexAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(index);
    }

    private sealed class StubMetadataSource : ITrackMetadataSource
    {
        private readonly Dictionary<string, TrackMetadataRow> _rows;

        public StubMetadataSource(IEnumerable<TrackMetadataRow> rows) =>
            _rows = rows.ToDictionary(row => row.TrackId, StringComparer.Ordinal);

        public Task<TrackMetadataRow?> FindByTrackIdAsync(
            string trackId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_rows.TryGetValue(trackId, out TrackMetadataRow? row) ? row : null);

        public Task<IReadOnlyDictionary<string, TrackMetadataRow>> FindByTrackIdsAsync(
            IReadOnlyCollection<string> trackIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, TrackMetadataRow>>(trackIds
                .Where(_rows.ContainsKey)
                .ToDictionary(id => id, id => _rows[id], StringComparer.Ordinal));
    }

    private sealed class StubCoOccurrenceSource(IReadOnlyList<CoOccurringTrack> tracks) : ITrackCoOccurrenceSource
    {
        public Task<IReadOnlyList<CoOccurringTrack>> FindCoOccurringAsync(
            string seedTrackId, int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CoOccurringTrack>>(tracks.Take(limit).ToArray());
    }
}
