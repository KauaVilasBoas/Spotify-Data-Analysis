using System.Data;
using System.Diagnostics;
using Dapper;
using Microsoft.Extensions.Configuration;
using SpotifyDataAnalysis.Infrastructure.Data;
using SpotifyDataAnalysis.Modules.Prediction.Application.Recommendations;
using SpotifyDataAnalysis.Modules.Prediction.Application.Recommendations.Evaluation;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Blending;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Deduplication;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Evaluation;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Recommendations.Evaluation;

/// <summary>
/// Prepara UMA vez o mundo da avaliação do recomendador contra o Postgres real: monta o índice de similaridade do
/// catálogo inteiro, carrega as amostras fixas, o censo e os insumos de pós-processamento. É fixture de coleção
/// porque montar o índice varre ~90k faixas — refazer isso por método de teste transformaria a suíte de qualidade
/// num teste de paciência.
///
/// <para><b>O que o E4.8 acrescentou:</b> o E4.4 mediu o ranking CRU, porque dedup (E4.7) e blend (E4.6) ainda não
/// existiam. Hoje os dois estão no caminho do endpoint, então o harness também carrega o que eles consomem — a
/// chave "artista|título" e a popularidade de cada faixa, e os vizinhos colaborativos de cada semente —, além de
/// amostras de duplicatas SEM o teto de 8 membros por grupo, que era o que zerava artificialmente o indicador de
/// "top-10 100% duplicado".</para>
///
/// <para><b>Credenciais nunca no repositório:</b> a connection string vem exclusivamente da variável de ambiente
/// <c>ConnectionStrings__SpotifyDb</c>. Sem ela, os testes que dependem de banco são PULADOS com motivo explícito
/// (<see cref="PostgresFactAttribute"/>) — <c>dotnet test</c> continua verde numa máquina limpa, e dá números
/// reais onde há catálogo.</para>
/// </summary>
public sealed class RecommenderEvaluationHarness : IAsyncLifetime
{
    /// <summary>Semente textual do sorteio. Fixa por contrato: é ela que torna a comparação entre pesos pareada.</summary>
    public const string SamplingSeed = "e4.4-proxies-v1";

    /// <summary>Sementes sorteadas para os proxies 1 e 2.</summary>
    public const int SeedSampleSize = 300;

    /// <summary>Grupos de quase-duplicatas sorteados para o proxy 3.</summary>
    public const int DuplicateGroupCount = 300;

    /// <summary>Teto de membros por grupo do E4.4, preservado só para reproduzir o número antigo lado a lado.</summary>
    public const int MaxMembersPerGroup = 8;

    /// <summary>
    /// Mínimo de membros para um grupo entrar em <see cref="LargeDuplicateGroupSample"/>.
    ///
    /// <para><b>Onze, e não um número redondo qualquer:</b> com top-K 10, uma semente só CONSEGUE ter o top-K
    /// inteiro tomado por irmãos se o grupo dela tiver pelo menos 10 outros membros. O teto de 8 do E4.4 tornava
    /// isso aritmeticamente impossível, e foi por isso que o indicador deu 0 — por construção, não por qualidade.</para>
    /// </summary>
    public const int LargeGroupMinimumMembers = 11;

    /// <summary>Top-N avaliado — o mesmo default do endpoint de recomendações.</summary>
    public const int TopN = 10;

    private const string ConnectionStringVariable = "ConnectionStrings__SpotifyDb";

    /// <summary>
    /// Quantos vizinhos colaborativos carregar por semente: o MESMO over-fetch que o handler de produção pede
    /// (<c>max(limit × 3, 10)</c>), senão o blend medido partiria de um conjunto de candidatas menor que o real.
    /// </summary>
    private static readonly int CollaborativeFetchCount = Math.Max(
        TopN * RecommenderQualityEvaluator.OverFetchFactor, RecommenderQualityEvaluator.MinimumOverFetch);

    /// <summary>Nome, artista principal e popularidade de cada faixa — o insumo da chave de dedup do E4.7.</summary>
    private const string TrackAttributesSql =
        """
        SELECT t.id                        AS "TrackId",
               t.name                      AS "Name",
               t.artists -> 0 ->> 'Name'   AS "Artist",
               t.popularity                AS "Popularity"
        FROM catalog.tracks AS t;
        """;

    /// <summary>A connection string do ambiente, ou <c>null</c> quando não há banco configurado.</summary>
    public static string? ConnectionStringOrNull =>
        Environment.GetEnvironmentVariable(ConnectionStringVariable) is { Length: > 0 } value ? value : null;

    /// <summary>O índice montado sobre o catálogo real.</summary>
    public SimilarityIndex Index { get; private set; } = null!;

    /// <summary>A amostra fixa do E4.4 (teto de 8 membros por grupo) — a que torna a comparação pareada.</summary>
    public RecommenderEvaluationSample Sample { get; private set; } = null!;

    /// <summary>Os MESMOS 300 grupos da amostra fixa, agora com todos os membros (sem o teto de 8).</summary>
    public RecommenderEvaluationSample UncappedDuplicateSample { get; private set; } = null!;

    /// <summary>Todos os grupos com <see cref="LargeGroupMinimumMembers"/>+ faixas — onde a dor do dedup de fato mora.</summary>
    public RecommenderEvaluationSample LargeDuplicateGroupSample { get; private set; } = null!;

    /// <summary>Os insumos de dedup e blend que o top-N de produção consome.</summary>
    public RecommenderEvaluationContext Context { get; private set; } = null!;

    /// <summary>Quantas das <see cref="SeedSampleSize"/> sementes têm alguma co-ocorrência registrada.</summary>
    public int SeedsWithCollaborativeCoverage { get; private set; }

    /// <summary>
    /// O subconjunto da amostra fixa cujas sementes TÊM co-ocorrência registrada — o único lugar onde o blend
    /// muda alguma coisa.
    ///
    /// <para><b>Por que medir o blend duas vezes:</b> sobre as 300 sementes, a maioria não tem sinal colaborativo e
    /// cai no content puro, então a média diluída responde "quanto o blend muda a experiência média" — mas esconde
    /// "o que o blend faz quando age". São duas perguntas, e a segunda é a que decide se o peso default presta.</para>
    /// </summary>
    public RecommenderEvaluationSample CollaborativeCoveredSample { get; private set; } = null!;

    /// <summary>O censo do catálogo elegível e das duplicatas.</summary>
    public CatalogDuplicateCensus Census { get; private set; } = null!;

    /// <summary>Quanto tempo a montagem do índice levou — parte do custo que o card manda registrar.</summary>
    public TimeSpan IndexBuildDuration { get; private set; }

    /// <summary>Memória gerenciada retida pelo índice, em MB, medida após coleta completa.</summary>
    public double IndexFootprintMb { get; private set; }

    /// <summary>Memória gerenciada retida pelos insumos de dedup/blend do E4.8, em MB — o custo acrescentado à medição.</summary>
    public double ContextFootprintMb { get; private set; }

    /// <summary>Pico de working set do processo durante a preparação, em MB — o número que importa num host de 256 MB.</summary>
    public double PeakWorkingSetMb => Process.GetCurrentProcess().PeakWorkingSet64 / (1024.0 * 1024.0);

    public async Task InitializeAsync()
    {
        string? connectionString = ConnectionStringOrNull;
        if (connectionString is null)
            return;

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SpotifyDb"] = connectionString
            })
            .Build();

        var connectionFactory = new DbConnectionFactory(configuration);
        var featureSource = new CatalogSimilarityFeatureSource(connectionFactory);
        var sampleSource = new CatalogRecommenderEvaluationSampleSource(connectionFactory);
        var coOccurrenceSource = new CatalogTrackCoOccurrenceSource(connectionFactory);

        long memoryBefore = GC.GetTotalMemory(forceFullCollection: true);
        long startTimestamp = Stopwatch.GetTimestamp();

        var rawTracks = new List<RawTrackFeatures>(capacity: 100_000);
        await foreach (RawTrackFeatures track in featureSource.StreamEligibleTracksAsync(20_000))
            rawTracks.Add(track);

        Index = SimilarityIndex.Build(rawTracks);
        IndexBuildDuration = Stopwatch.GetElapsedTime(startTimestamp);

        rawTracks.Clear();
        rawTracks.TrimExcess();

        long memoryAfterIndex = GC.GetTotalMemory(forceFullCollection: true);
        IndexFootprintMb = Math.Max(0, memoryAfterIndex - memoryBefore) / (1024.0 * 1024.0);

        Sample = await sampleSource.LoadSampleAsync(
            SamplingSeed, SeedSampleSize, DuplicateGroupCount, MaxMembersPerGroup);

        // TODOS os grupos de duplicatas, com todos os membros. Uma leitura só serve às duas amostras sem teto: a
        // dos MESMOS 300 grupos da amostra fixa (comparação pareada com o E4.4) e a dos grupos grandes, onde o
        // indicador de "top-K 100% duplicado" deixa de ser zero por construção.
        RecommenderEvaluationSample allGroups = await sampleSource.LoadSampleAsync(
            SamplingSeed, SeedSampleSize, duplicateGroupCount: int.MaxValue, maxMembersPerGroup: int.MaxValue);

        var sampledMatchKeys = new HashSet<string>(
            Sample.DuplicateGroups.Select(group => group.MatchKey), StringComparer.Ordinal);

        UncappedDuplicateSample = RecommenderEvaluationSample.Create(
            Sample.SeedTrackIds,
            allGroups.DuplicateGroups.Where(group => sampledMatchKeys.Contains(group.MatchKey)).ToArray());

        DuplicateTrackGroup[] largeGroups = allGroups.DuplicateGroups
            .Where(group => group.TrackIds.Count >= LargeGroupMinimumMembers)
            .ToArray();

        LargeDuplicateGroupSample = RecommenderEvaluationSample.Create(
            largeGroups.Select(group => group.TrackIds[0]).ToArray(), largeGroups);

        Census = await sampleSource.LoadCensusAsync();

        long memoryBeforeContext = GC.GetTotalMemory(forceFullCollection: true);
        Context = await LoadContextAsync(connectionFactory, coOccurrenceSource);
        long memoryAfterContext = GC.GetTotalMemory(forceFullCollection: true);
        ContextFootprintMb = Math.Max(0, memoryAfterContext - memoryBeforeContext) / (1024.0 * 1024.0);

        CollaborativeCoveredSample = RecommenderEvaluationSample.Create(
            Sample.SeedTrackIds.Where(seed => Context.CollaborativeFor(seed).Count > 0).ToArray(),
            Sample.DuplicateGroups);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Carrega o que o pós-processamento de produção consome: a chave de dedup + popularidade de cada faixa e os
    /// vizinhos colaborativos de cada semente da amostra.
    ///
    /// <para>Nome e artista crus são DESCARTADOS depois de derivar a chave — reter os dois seria uma segunda cópia
    /// do catálogo em memória, exatamente o que a restrição de free tier proíbe.</para>
    /// </summary>
    private async Task<RecommenderEvaluationContext> LoadContextAsync(
        DbConnectionFactory connectionFactory, CatalogTrackCoOccurrenceSource coOccurrenceSource)
    {
        using IDbConnection connection = await connectionFactory.CreateOpenConnectionAsync();

        var attributes = new Dictionary<string, EvaluationTrackAttributes>(
            (int)Census.EligibleTracks, StringComparer.Ordinal);

        foreach (TrackAttributeRow row in await connection.QueryAsync<TrackAttributeRow>(TrackAttributesSql))
        {
            attributes[row.TrackId] = new EvaluationTrackAttributes(
                RecommendationDuplicateKey.From(row.Name, row.Artist), row.Popularity);
        }

        var collaborative = new Dictionary<string, IReadOnlyList<BlendCollaborativeCandidate>>(
            Sample.SeedTrackIds.Count, StringComparer.Ordinal);

        foreach (string seedTrackId in Sample.SeedTrackIds)
        {
            IReadOnlyList<CoOccurringTrack> coOccurring =
                await coOccurrenceSource.FindCoOccurringAsync(seedTrackId, CollaborativeFetchCount);

            if (coOccurring.Count == 0)
                continue;

            SeedsWithCollaborativeCoverage++;
            collaborative[seedTrackId] = coOccurring
                .Select(track => new BlendCollaborativeCandidate(track.TrackId, track.CoPlaylists, track.Jaccard))
                .ToArray();
        }

        return new RecommenderEvaluationContext(attributes, collaborative);
    }

    private sealed record TrackAttributeRow(string TrackId, string? Name, string? Artist, int? Popularity);
}

/// <summary>
/// Um <see cref="FactAttribute"/> que se auto-pula quando não há Postgres configurado. A alternativa — um teste que
/// falha sem banco — tornaria a suíte vermelha por ausência de ambiente, que é ruído, não regressão; e a
/// alternativa oposta — não ter o teste — deixaria a avaliação sem execução verificável.
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (RecommenderEvaluationHarness.ConnectionStringOrNull is null)
            Skip = "Requer Postgres real: defina a variável de ambiente ConnectionStrings__SpotifyDb.";
    }
}

/// <summary>
/// Coleção que compartilha UMA instância do <see cref="RecommenderEvaluationHarness"/> entre todas as classes de
/// avaliação do recomendador.
///
/// <para><b>Não é detalhe de organização, é a restrição de custo zero:</b> com <c>IClassFixture</c>, cada classe de
/// teste ganharia o SEU harness e montaria o seu próprio índice — duas cópias do catálogo vivas ao mesmo tempo. Foi
/// exatamente o que a medição de custo flagrou (82,7 MB retidos e 229 MB de pico, contra 39,7 MB e 137 MB com uma
/// cópia só). Num host de 256 MB isso é a diferença entre rodar e ser morto por OOM, e é justamente o que o card
/// proíbe: "não alocar segunda cópia do catálogo em memória".</para>
/// </summary>
[CollectionDefinition(Name)]
public sealed class RecommenderEvaluationCollection : ICollectionFixture<RecommenderEvaluationHarness>
{
    /// <summary>Nome da coleção; as classes de avaliação o referenciam em <c>[Collection]</c>.</summary>
    public const string Name = "Avaliação do recomendador (E4.4/E4.8)";
}
