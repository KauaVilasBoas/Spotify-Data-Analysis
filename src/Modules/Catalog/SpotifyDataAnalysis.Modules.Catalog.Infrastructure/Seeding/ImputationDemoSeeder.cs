using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
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
    long TracksImputed,
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

    /// <summary>Origem gravada nas features imputadas — distingue do dataset medido.</summary>
    private const string SourceName = "kaggle:spotify-tracks-dataset";

    /// <summary>Gênero das faixas semeadas — deve existir no CSV de medianas para que a imputação use a mediana por gênero.</summary>
    private const string SeedGenre = "pop";

    private readonly CatalogDbContext _dbContext;
    private readonly ILogger<ImputationDemoSeeder> _logger;

    private static readonly Action<ILogger, Exception?> LogMedianProfileBuilt =
        LoggerMessage.Define(LogLevel.Information, new EventId(1, nameof(LogMedianProfileBuilt)),
            "Perfil de medianas construído. Prosseguindo com a semeadura de imputação.");

    private static readonly Action<ILogger, long, long, long, long, Exception?> LogSeedComplete =
        LoggerMessage.Define<long, long, long, long>(LogLevel.Information, new EventId(2, nameof(LogSeedComplete)),
            "Seed de imputação: {Inserted} faixas inseridas, {AlreadyExisted} já existiam, " +
            "{Imputed} imputadas em {ElapsedMs} ms.");

    public ImputationDemoSeeder(CatalogDbContext dbContext, ILogger<ImputationDemoSeeder> logger)
    {
        _dbContext = dbContext;
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
        // É exatamente o que ImportKaggleAudioFeaturesCommandHandler faz na primeira passada.
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
        // Recarrega do banco para ter os agregados rastreados (EF precisa rastrear para detectar mudanças).
        // O ITrackRepository.GetByIdAsync seria a porta correta num handler, mas aqui usamos o DbContext
        // diretamente seguindo o padrão dos seeders vizinhos.
        long imputed = 0;

        foreach (KaggleAudioFeaturesRow row in demoRows)
        {
            string normalizedId = row.TrackId.Trim();

            Track? track = await _dbContext.Tracks
                .FindAsync([SpotifyTrackId.Of(normalizedId)], cancellationToken);

            if (track is null)
                continue;

            // Aqui está o caminho de produção: o MedianAudioFeatureImputer.Impute() real,
            // com o perfil de medianas construído pelo AudioFeatureMedianProfileBuilder real.
            ImputedAudioFeatures result = new MedianAudioFeatureImputer().Impute(row, medians);

            track.AttachAudioFeatures(AudioFeatures.Create(
                danceability: result[AudioFeature.Danceability],
                energy: result[AudioFeature.Energy],
                valence: result[AudioFeature.Valence],
                tempo: result[AudioFeature.Tempo],
                acousticness: result[AudioFeature.Acousticness],
                instrumentalness: result[AudioFeature.Instrumentalness],
                liveness: result[AudioFeature.Liveness],
                speechiness: result[AudioFeature.Speechiness],
                loudness: result[AudioFeature.Loudness],
                key: (int)result[AudioFeature.Key],
                mode: (int)result[AudioFeature.Mode],
                timeSignature: (int)result[AudioFeature.TimeSignature],
                source: SourceName,
                genre: row.Genre,
                isImputed: result.IsImputed));

            if (result.IsImputed)
                imputed++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.ChangeTracker.Clear();

        stopwatch.Stop();

        var seedResult = new ImputationDemoSeedResult(
            TracksInserted: inserted,
            TracksAlreadyExisted: alreadyExisted,
            TracksImputed: imputed,
            ElapsedMilliseconds: (long)stopwatch.Elapsed.TotalMilliseconds);

        LogSeedComplete(_logger,
            seedResult.TracksInserted, seedResult.TracksAlreadyExisted,
            seedResult.TracksImputed, seedResult.ElapsedMilliseconds, null);

        return seedResult;
    }

    /// <summary>
    /// Constrói o perfil de medianas a partir do CSV do Kaggle — a primeira passada do
    /// <see cref="ImportKaggleAudioFeaturesCommandHandler"/>.
    /// </summary>
    private static async Task<AudioFeatureMedianProfile> BuildMedianProfileAsync(
        string csvFilePath, CancellationToken cancellationToken)
    {
        var builder = new AudioFeatureMedianProfileBuilder();

        await foreach (KaggleAudioFeaturesRow row in ReadCsvAsync(csvFilePath, cancellationToken))
            builder.Observe(row);

        return builder.Build();
    }

    /// <summary>
    /// Lê o CSV de audio-features do Kaggle linha a linha, sem materializar tudo em memória.
    /// Replica o papel do <see cref="Application.Ingestion.IKaggleAudioFeaturesReader"/> sem depender
    /// da implementação de Infrastructure (que não está visível neste assembly).
    /// </summary>
    private static async IAsyncEnumerable<KaggleAudioFeaturesRow> ReadCsvAsync(
        string csvFilePath,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(csvFilePath, Encoding.UTF8);

        string? header = await reader.ReadLineAsync(cancellationToken);
        if (header is null)
            yield break;

        // Mapeia as colunas por nome (o dataset Kaggle tem a ordem documentada, mas casar por nome
        // é mais robusto e o que o KaggleAudioFeaturesCsvReader faz via CsvHelper).
        Dictionary<string, int> colIndex = BuildColumnIndex(header);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string[] cols = line.Split(',');

            yield return new KaggleAudioFeaturesRow(
                TrackId: Col(cols, colIndex, "track_id") ?? string.Empty,
                TrackName: Col(cols, colIndex, "track_name"),
                Artists: Col(cols, colIndex, "artists"),
                Genre: Col(cols, colIndex, "track_genre"),
                DurationMs: ColInt(cols, colIndex, "duration_ms"),
                Danceability: ColDouble(cols, colIndex, "danceability"),
                Energy: ColDouble(cols, colIndex, "energy"),
                Valence: ColDouble(cols, colIndex, "valence"),
                Tempo: ColDouble(cols, colIndex, "tempo"),
                Acousticness: ColDouble(cols, colIndex, "acousticness"),
                Instrumentalness: ColDouble(cols, colIndex, "instrumentalness"),
                Liveness: ColDouble(cols, colIndex, "liveness"),
                Speechiness: ColDouble(cols, colIndex, "speechiness"),
                Loudness: ColDouble(cols, colIndex, "loudness"),
                Key: ColInt(cols, colIndex, "key"),
                Mode: ColInt(cols, colIndex, "mode"),
                TimeSignature: ColInt(cols, colIndex, "time_signature"));
        }
    }

    private static Dictionary<string, int> BuildColumnIndex(string headerLine)
    {
        string[] headers = headerLine.Split(',');
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Length; i++)
            index[headers[i].Trim('"').Trim()] = i;
        return index;
    }

    private static string? Col(string[] cols, Dictionary<string, int> idx, string name)
    {
        if (!idx.TryGetValue(name, out int i) || i >= cols.Length)
            return null;
        string v = cols[i].Trim('"').Trim();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    private static double? ColDouble(string[] cols, Dictionary<string, int> idx, string name)
    {
        string? v = Col(cols, idx, name);
        return v is not null && double.TryParse(v, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double d) ? d : null;
    }

    private static int? ColInt(string[] cols, Dictionary<string, int> idx, string name)
    {
        string? v = Col(cols, idx, name);
        return v is not null && int.TryParse(v, out int n) ? n : null;
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
