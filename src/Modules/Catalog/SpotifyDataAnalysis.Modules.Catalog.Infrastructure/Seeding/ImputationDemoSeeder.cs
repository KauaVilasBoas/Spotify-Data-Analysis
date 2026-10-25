using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;
using SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion.Imputation;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Common;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;
using SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Persistence;

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Seeding;

/// <summary>Censo de uma execução do seed de imputação.</summary>
public sealed record ImputationDemoSeedResult(
    long TracksInserted,
    long TracksAlreadyExisted,
    /// <summary>
    /// Conta quantas faixas do conjunto demo (inseridas OU já existentes) tiveram pelo menos
    /// uma feature imputada nesta execução. Na segunda execução idempotente este número é 6
    /// porque o imputador re-processa as existentes — se quiser contar só as recém-inseridas,
    /// use <see cref="TracksInserted"/>.
    /// </summary>
    long TracksProcessedByImputer,
    long ElapsedMilliseconds);

/// <summary>
/// Semente de demonstração de imputação (E1.13): insere um conjunto pequeno e identificável de faixas
/// <b>sem audio-features</b> e passa-as pelo caminho de produção real —
/// <see cref="AudioFeatureMedianProfileBuilder"/> + <see cref="MedianAudioFeatureImputer"/> —
/// para que o path de imputação seja exercitado por dado real (não por UPDATE direto).
///
/// <para>Os <c>track_id</c>s são prefixados com <c>imp_seed_</c>, tornando o conjunto <b>identificável</b>
/// por predicado SQL, <b>reversível</b> (<c>DELETE FROM catalog.tracks WHERE id LIKE 'imp_seed_%'</c>) e
/// <b>disjunto</b> dos 89.740 existentes (que não começam com esse prefixo).</para>
///
/// <para><b>Idempotente:</b> faixas já existentes (pelo id normalizado) são puladas; o perfil de medianas
/// é sempre reconstruído a partir do CSV passado em <paramref name="kaggleAudioFeaturesCsvPath"/>; a
/// segunda execução produz as mesmas contagens.</para>
///
/// <para><b>Escreve pelo <see cref="CatalogDbContext"/> cru</b> — o mesmo padrão dos seeders vizinhos.
/// O dispatch de domain events / escrita no Outbox só ocorre no UnitOfWork; aqui é carga de dados,
/// não ingestão de negócio.</para>
/// </summary>
public sealed class ImputationDemoSeeder
{
    /// <summary>
    /// Prefixo que marca estas faixas como sintéticas de demonstração.
    /// <b>Predicado de não-regressão:</b> <c>WHERE id NOT LIKE 'imp_seed_%'</c> isola as faixas reais.
    /// </summary>
    public const string TrackIdPrefix = "imp_seed_";

    /// <summary>Gênero das faixas semeadas — deve existir no CSV de medianas para que a imputação use a mediana por gênero.</summary>
    private const string SeedGenre = "pop";

    private readonly CatalogDbContext _dbContext;
    private readonly IKaggleAudioFeaturesReader _reader;
    private readonly ILogger<ImputationDemoSeeder> _logger;

    private static readonly Action<ILogger, Exception?> LogMedianProfileBuilt =
        LoggerMessage.Define(LogLevel.Information, new EventId(1, nameof(LogMedianProfileBuilt)),
            "Perfil de medianas construído. Prosseguindo com a semeadura de imputação.");

    private static readonly Action<ILogger, long, long, long, long, Exception?> LogSeedComplete =
        LoggerMessage.Define<long, long, long, long>(LogLevel.Information, new EventId(2, nameof(LogSeedComplete)),
            "Seed de imputação: {Inserted} faixas inseridas, {AlreadyExisted} já existiam, " +
            "{Imputed} imputadas em {ElapsedMs} ms.");

    public ImputationDemoSeeder(
        CatalogDbContext dbContext,
        IKaggleAudioFeaturesReader reader,
        ILogger<ImputationDemoSeeder> logger)
    {
        _dbContext = dbContext;
        _reader = reader;
        _logger = logger;
    }

    /// <summary>
    /// Semeia as faixas de demonstração e aplica o imputador real.
    /// </summary>
    /// <param name="kaggleAudioFeaturesCsvPath">
    /// Caminho do CSV do Kaggle com audio-features (o mesmo lido pelo
    /// <c>ImportKaggleAudioFeaturesCommand</c>). É a fonte do perfil de medianas — a imputação usa as
    /// medianas reais do dataset, não valores fabricados.
    /// </param>
    public async Task<ImputationDemoSeedResult> SeedAsync(
        string kaggleAudioFeaturesCsvPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(kaggleAudioFeaturesCsvPath))
            throw new FileNotFoundException(
                $"CSV de audio-features não encontrado: {kaggleAudioFeaturesCsvPath}",
                kaggleAudioFeaturesCsvPath);

        Stopwatch stopwatch = Stopwatch.StartNew();

        // --- Passo 1: Construir o perfil de medianas a partir do dataset real -------------------------
        // Usa IKaggleAudioFeaturesReader (a porta registrada no Composition Root) para que a leitura
        // seja idêntica à que ImportKaggleAudioFeaturesCommandHandler usa — inclusive em encoding,
        // tratamento de aspas e qualquer futuro ajuste centralizado na implementação concreta.
        AudioFeatureMedianProfile medians = await BuildMedianProfileAsync(
            kaggleAudioFeaturesCsvPath, cancellationToken);

        LogMedianProfileBuilt(_logger, null);

        // --- Passo 2: Definir as faixas de demonstração -----------------------------------------------
        // Conjunto pequeno e identificável. Cada linha tem pelo menos uma feature null para garantir
        // que o imputador seja acionado. As primeiras (ex.: imp_seed_01..05) têm várias features
        // propositalmente ausentes; as últimas têm apenas uma — para cobrir cenários distintos.
        IReadOnlyList<KaggleAudioFeaturesRow> demoRows = BuildDemoRows();

        // --- Passo 3: Idempotência — descobre quais já existem no banco --------------------------------
        // Os ids dos demoRows são fixos e conhecidos; filtrar por lista é traduzível pelo EF (IN (...)).
        List<SpotifyTrackId> demoIds = demoRows.Select(r => SpotifyTrackId.Of(r.TrackId)).ToList();
        var existingIds = new HashSet<string>(
            await _dbContext.Tracks.AsNoTracking()
                .Where(t => demoIds.Contains(t.Id))
                .Select(t => t.Id.Value)
                .ToListAsync(cancellationToken),
            StringComparer.Ordinal);

        long alreadyExisted = existingIds.Count;

        // --- Passo 4: Inserir as faixas sem audio-features -------------------------------------------
        // O seeder cria as faixas agora; o imputador preencherá as features logo após.
        var tracksToImpute = new List<Track>();

        foreach (KaggleAudioFeaturesRow row in demoRows)
        {
            string normalizedId = row.TrackId.Trim();
            if (existingIds.Contains(normalizedId))
                continue;

            Track track = Track.Register(
                SpotifyTrackId.Of(normalizedId),
                name: row.TrackName ?? normalizedId,
                popularity: Popularity.Of(50),
                durationMs: row.DurationMs ?? 200_000,
                @explicit: false,
                albumId: null,
                artists: Array.Empty<TrackArtist>(),
                isrc: null);

            tracksToImpute.Add(track);
        }

        if (tracksToImpute.Count > 0)
        {
            await _dbContext.Tracks.AddRangeAsync(tracksToImpute, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();
        }

        long inserted = tracksToImpute.Count;

        // --- Passo 5: Aplicar o imputador real -------------------------------------------------------
        // Carrega todas as faixas de demonstração em lote (um único SELECT IN) para que o
        // ChangeTracker as rastreie, evitando N queries individuais. O imputer é criado uma única vez
        // fora do laço.
        Dictionary<string, Track> tracksById = (await _dbContext.Tracks
            .Where(t => demoIds.Contains(t.Id))
            .ToListAsync(cancellationToken))
            .ToDictionary(t => t.Id.Value, StringComparer.Ordinal);

        var imputer = new MedianAudioFeatureImputer();
        long processedByImputer = 0;

        foreach (KaggleAudioFeaturesRow row in demoRows)
        {
            string normalizedId = row.TrackId.Trim();

            if (!tracksById.TryGetValue(normalizedId, out Track? track))
                continue;

            ImputedAudioFeatures result = imputer.Impute(row, medians);
            track.AttachAudioFeatures(AudioFeaturesFactory.Build(result, row.Genre));

            if (result.IsImputed)
                processedByImputer++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.ChangeTracker.Clear();

        stopwatch.Stop();

        var seedResult = new ImputationDemoSeedResult(
            TracksInserted: inserted,
            TracksAlreadyExisted: alreadyExisted,
            TracksProcessedByImputer: processedByImputer,
            ElapsedMilliseconds: (long)stopwatch.Elapsed.TotalMilliseconds);

        LogSeedComplete(_logger,
            seedResult.TracksInserted, seedResult.TracksAlreadyExisted,
            seedResult.TracksProcessedByImputer, seedResult.ElapsedMilliseconds, null);

        return seedResult;
    }

    /// <summary>
    /// Constrói o perfil de medianas a partir do CSV do Kaggle — a primeira passada do
    /// <see cref="ImportKaggleAudioFeaturesCommandHandler"/>. Usa a porta
    /// <see cref="IKaggleAudioFeaturesReader"/> para que qualquer ajuste na abertura do arquivo
    /// (encoding, tratamento de aspas, gzip) seja centralizado na implementação concreta.
    /// </summary>
    private async Task<AudioFeatureMedianProfile> BuildMedianProfileAsync(
        string csvFilePath, CancellationToken cancellationToken)
    {
        var builder = new AudioFeatureMedianProfileBuilder();

        await foreach (KaggleAudioFeaturesRow row in _reader.ReadAsync(csvFilePath, cancellationToken))
            builder.Observe(row);

        return builder.Build();
    }

    /// <summary>
    /// Define as faixas de demonstração. Cada linha tem pelo menos uma feature <see langword="null"/>
    /// para garantir que o <see cref="MedianAudioFeatureImputer"/> seja acionado.
    ///
    /// <para>Gênero = "pop" — existe no dataset real, então a imputação usa a mediana por gênero
    /// (o caminho preferencial, não o fallback global).</para>
    /// </summary>
    private static IReadOnlyList<KaggleAudioFeaturesRow> BuildDemoRows()
        =>
        [
            // Faixas com múltiplas features ausentes (cenário rico de imputação)
            new(TrackId: "imp_seed_01", TrackName: "Demo Track 01", Artists: "Demo Artist",
                Genre: SeedGenre, DurationMs: 210_000,
                Danceability: null,  Energy: null,  Valence: null,
                Tempo: null,         Acousticness: null, Instrumentalness: null,
                Liveness: null,      Speechiness: null,  Loudness: null,
                Key: null,           Mode: null,    TimeSignature: null),

            new(TrackId: "imp_seed_02", TrackName: "Demo Track 02", Artists: "Demo Artist",
                Genre: SeedGenre, DurationMs: 195_000,
                Danceability: null,  Energy: null,  Valence: 0.55,
                Tempo: null,         Acousticness: 0.08, Instrumentalness: null,
                Liveness: null,      Speechiness: null,  Loudness: null,
                Key: null,           Mode: 1,       TimeSignature: null),

            new(TrackId: "imp_seed_03", TrackName: "Demo Track 03", Artists: "Demo Artist",
                Genre: SeedGenre, DurationMs: 230_000,
                Danceability: 0.72, Energy: null,   Valence: null,
                Tempo: 128.0,       Acousticness: null, Instrumentalness: 0.0,
                Liveness: null,     Speechiness: null,  Loudness: null,
                Key: 5,             Mode: null,     TimeSignature: 4),

            // Faixas com apenas uma feature ausente (cenário mínimo de imputação)
            new(TrackId: "imp_seed_04", TrackName: "Demo Track 04", Artists: "Demo Artist",
                Genre: SeedGenre, DurationMs: 180_000,
                Danceability: 0.65, Energy: 0.80,  Valence: 0.40,
                Tempo: 120.0,       Acousticness: 0.05, Instrumentalness: 0.0,
                Liveness: 0.10,     Speechiness: 0.04,  Loudness: null,
                Key: 2,             Mode: 1,        TimeSignature: 4),

            new(TrackId: "imp_seed_05", TrackName: "Demo Track 05", Artists: "Demo Artist",
                Genre: SeedGenre, DurationMs: 250_000,
                Danceability: 0.55, Energy: 0.70,  Valence: 0.60,
                Tempo: 110.0,       Acousticness: 0.15, Instrumentalness: 0.01,
                Liveness: 0.12,     Speechiness: 0.06,  Loudness: -7.5,
                Key: null,          Mode: 0,        TimeSignature: 4),

            // Faixas com um gênero diferente (sem mediana específica → cai no global)
            new(TrackId: "imp_seed_06", TrackName: "Demo Track 06", Artists: "Demo Artist",
                Genre: "genero-inexistente-xyz", DurationMs: 200_000,
                Danceability: null,  Energy: null,  Valence: null,
                Tempo: null,         Acousticness: null, Instrumentalness: null,
                Liveness: null,      Speechiness: null,  Loudness: null,
                Key: null,           Mode: null,    TimeSignature: null),
        ];
}
