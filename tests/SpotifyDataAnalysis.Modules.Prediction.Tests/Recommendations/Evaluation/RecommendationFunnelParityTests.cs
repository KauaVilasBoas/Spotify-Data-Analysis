using SpotifyDataAnalysis.Modules.Prediction.Application.Recommendations;
using SpotifyDataAnalysis.Modules.Prediction.Contracts.Recommendations;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;
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

    // --- fixture ---

    private static async Task<string[]> EndpointRanking(Fixture fixture, bool dedupe, bool boost)
    {
        var handler = new GetTrackRecommendationsQueryHandler(
            new StubIndexProvider(fixture.Index), new StubMetadataSource(fixture.Rows), new EmptyCoOccurrenceSource());

        TrackRecommendationsResponse response = await handler.HandleAsync(new GetTrackRecommendationsQuery(
            "seed", Limit, ExplainTopK: 3,
            boost ? GenreRankingModeContract.Boost : GenreRankingModeContract.Off,
            dedupe, RecommendationStrategyContract.Content, GetTrackRecommendationsQuery.DefaultBlendWeight));

        return response.Recommendations.Select(item => item.TrackId).ToArray();
    }

    private static EvaluatorResult EvaluatorRanking(Fixture fixture, bool dedupe, bool boost)
    {
        RecommenderEvaluationSetting setting = (boost
                ? RecommenderEvaluationSetting.BoostedBy(GenreAffinityPolicy.DefaultBoostWeight)
                : RecommenderEvaluationSetting.CosineOnly())
            .WithDedupe(dedupe);

        var attributes = fixture.Rows.ToDictionary(
            row => row.TrackId,
            row => new EvaluationTrackAttributes(RecommendationDuplicateKey.From(row.Name, row.Artist), row.Popularity),
            StringComparer.Ordinal);

        var context = new RecommenderEvaluationContext(
            attributes, new Dictionary<string, IReadOnlyList<SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Blending.BlendCollaborativeCandidate>>());

        EndpointTopN top = RecommenderQualityEvaluator.RankAsEndpointWould(
            fixture.Index, "seed", Limit, setting, context)!;

        return new EvaluatorResult(top.Neighbors.Select(neighbor => neighbor.TrackId).ToArray(), top.RoundsUsed);
    }

    private sealed record EvaluatorResult(string[] Ids, int RoundsUsed);

    private sealed record Fixture(
        SimilarityIndex Index, IReadOnlyList<TrackMetadataRow> Rows, IReadOnlyDictionary<string, string> NameOf);

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
            SimilarityIndex.Build(catalog), rows, rows.ToDictionary(row => row.TrackId, row => row.Name!, StringComparer.Ordinal));
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

    private sealed class EmptyCoOccurrenceSource : ITrackCoOccurrenceSource
    {
        public Task<IReadOnlyList<CoOccurringTrack>> FindCoOccurringAsync(
            string seedTrackId, int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CoOccurringTrack>>([]);
    }
}
