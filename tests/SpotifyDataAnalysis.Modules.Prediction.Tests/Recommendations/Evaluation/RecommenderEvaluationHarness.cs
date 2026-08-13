using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using SpotifyDataAnalysis.Infrastructure.Data;
using SpotifyDataAnalysis.Modules.Prediction.Application.Recommendations;
using SpotifyDataAnalysis.Modules.Prediction.Application.Recommendations.Evaluation;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Evaluation;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Recommendations.Evaluation;

/// <summary>
/// Prepara UMA vez o mundo da avaliação do E4.4 contra o Postgres real: monta o índice de similaridade do catálogo
/// inteiro, carrega a amostra fixa e lê o censo. É fixture de classe porque montar o índice varre ~90k faixas —
/// refazer isso por método de teste transformaria a suíte de qualidade num teste de paciência.
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

    /// <summary>Teto de membros por grupo, para coletâneas de dezenas de faixas não dominarem a média nem o custo.</summary>
    public const int MaxMembersPerGroup = 8;

    /// <summary>Top-N avaliado — o mesmo default do endpoint de recomendações.</summary>
    public const int TopN = 10;

    private const string ConnectionStringVariable = "ConnectionStrings__SpotifyDb";

    /// <summary>A connection string do ambiente, ou <c>null</c> quando não há banco configurado.</summary>
    public static string? ConnectionStringOrNull =>
        Environment.GetEnvironmentVariable(ConnectionStringVariable) is { Length: > 0 } value ? value : null;

    /// <summary>O índice montado sobre o catálogo real.</summary>
    public SimilarityIndex Index { get; private set; } = null!;

    /// <summary>A amostra fixa que atravessa todas as configurações medidas.</summary>
    public RecommenderEvaluationSample Sample { get; private set; } = null!;

    /// <summary>O censo do catálogo elegível e das duplicatas.</summary>
    public CatalogDuplicateCensus Census { get; private set; } = null!;

    /// <summary>Quanto tempo a montagem do índice levou — parte do custo que o card manda registrar.</summary>
    public TimeSpan IndexBuildDuration { get; private set; }

    /// <summary>Memória gerenciada retida pelo índice, em MB, medida após coleta completa.</summary>
    public double IndexFootprintMb { get; private set; }

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

        long memoryBefore = GC.GetTotalMemory(forceFullCollection: true);
        long startTimestamp = Stopwatch.GetTimestamp();

        var rawTracks = new List<RawTrackFeatures>(capacity: 100_000);
        await foreach (RawTrackFeatures track in featureSource.StreamEligibleTracksAsync(20_000))
            rawTracks.Add(track);

        Index = SimilarityIndex.Build(rawTracks);
        IndexBuildDuration = Stopwatch.GetElapsedTime(startTimestamp);

        rawTracks.Clear();
        rawTracks.TrimExcess();

        long memoryAfter = GC.GetTotalMemory(forceFullCollection: true);
        IndexFootprintMb = Math.Max(0, memoryAfter - memoryBefore) / (1024.0 * 1024.0);

        Sample = await sampleSource.LoadSampleAsync(
            SamplingSeed, SeedSampleSize, DuplicateGroupCount, MaxMembersPerGroup);

        Census = await sampleSource.LoadCensusAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;
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
/// avaliação do E4.4.
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
    public const string Name = "Avaliação do recomendador (E4.4)";
}
