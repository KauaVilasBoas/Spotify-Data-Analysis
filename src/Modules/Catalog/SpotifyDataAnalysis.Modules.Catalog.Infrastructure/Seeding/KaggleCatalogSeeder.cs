using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Common;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;
using SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Persistence;

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Seeding;

/// <summary>Censo de uma execução do seed: total lido, criadas, e o que ficou de fora e por quê.</summary>
public sealed record CatalogSeedResult(
    long TotalRows,
    long Created,
    long DuplicatesSkipped,
    long IncompleteSkipped,
    long ElapsedMilliseconds);

/// <summary>
/// Seed do catálogo a partir do dataset Kaggle (E1.10): cria faixas direto do CSV, com popularity +
/// audio-features MEDIDAS + gênero, para dar um dataset de treino real ao E3 sem depender da API do Spotify.
///
/// <para><b>Escreve pelo <see cref="CatalogDbContext"/> cru, em lotes</b> (<c>AddRange</c> + <c>SaveChanges</c>
/// + <c>ChangeTracker.Clear</c>), e NÃO pelo <c>EfUnitOfWork</c>: o dispatch de domain events / escrita no
/// Outbox só acontece no UnitOfWork, então semear ~89k faixas por aqui não despeja 89k <c>TrackRegistered</c>
/// no outbox. É carga de dados, não ingestão de negócio.</para>
///
/// <para><b>Dedupe por <c>track_id</c></b>: o dataset lista a mesma faixa uma vez por gênero, então a primeira
/// ocorrência vence (e fixa o gênero). Faixas incompletas — sem alguma grandeza numérica, popularity fora de
/// 0–100, ou id/nome vazio — são puladas e contadas. Créditos de artista ficam vazios de propósito: o CSV traz
/// NOME de artista, não um <c>SpotifyArtistId</c>, e fabricar um id seria inventar dado.</para>
/// </summary>
public sealed class KaggleCatalogSeeder
{
    /// <summary>Origem gravada nas features — rastreia de onde o dado veio (auditoria).</summary>
    private const string SourceName = "kaggle:spotify-tracks-dataset";

    /// <summary>Teto do nome da faixa = a coluna <c>name varchar(400)</c>. Um punhado de títulos do dataset
    /// passa disso; como o nome não é feature de ML, cortamos em vez de descartar a faixa (que tem popularity
    /// e audio-features úteis).</summary>
    private const int MaxTrackNameLength = 400;

    private readonly CatalogDbContext _dbContext;
    private readonly ILogger<KaggleCatalogSeeder> _logger;

    public KaggleCatalogSeeder(CatalogDbContext dbContext, ILogger<KaggleCatalogSeeder> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<CatalogSeedResult> SeedAsync(
        string csvFilePath, int batchSize = 5_000, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(csvFilePath))
            throw new FileNotFoundException($"CSV do Kaggle não encontrado: {csvFilePath}", csvFilePath);

        var seenTrackIds = new HashSet<string>(StringComparer.Ordinal);

        // Idempotência/resumo: os lotes commitam soltos (sem transação única, de propósito, para o pico de
        // memória não escalar). Se uma execução falhar no meio, a próxima pula o que já entrou em vez de
        // colidir na PK. Preenche o conjunto com os ids JÁ NORMALIZADOS do banco.
        foreach (SpotifyTrackId existingId in
                 await _dbContext.Tracks.AsNoTracking().Select(track => track.Id).ToListAsync(cancellationToken))
        {
            seenTrackIds.Add(existingId.Value);
        }

        var batch = new List<Track>(batchSize);
        long total = 0, created = 0, duplicates = 0, incomplete = 0;
        Stopwatch stopwatch = Stopwatch.StartNew();

        await foreach (KaggleCatalogRow row in KaggleCatalogCsvReader.ReadAsync(csvFilePath, cancellationToken))
        {
            total++;

            if (!TryCreateTrack(row, out Track? track))
            {
                incomplete++;
                continue;
            }

            // Dedupe pela id JÁ NORMALIZADA (track.Id.Value), não pela crua do CSV: SpotifyTrackId.Of faz
            // Trim, então dois track_ids que só diferem em espaço nas pontas viram a MESMA chave primária —
            // deduplicar pela crua deixaria os dois passarem e a 2ª inserção colidiria na pk_tracks (23505).
            if (!seenTrackIds.Add(track!.Id.Value))
            {
                duplicates++;
                continue;
            }

            batch.Add(track!);
            created++;

            if (batch.Count >= batchSize)
                await FlushAsync(batch, cancellationToken);
        }

        if (batch.Count > 0)
            await FlushAsync(batch, cancellationToken);

        stopwatch.Stop();
        var result = new CatalogSeedResult(total, created, duplicates, incomplete, (long)stopwatch.Elapsed.TotalMilliseconds);

        _logger.LogInformation(
            "Seed do catálogo: {Created} faixas criadas de {Total} linhas ({Duplicates} duplicadas, " +
            "{Incomplete} incompletas) em {ElapsedMs} ms.",
            result.Created, result.TotalRows, result.DuplicatesSkipped, result.IncompleteSkipped,
            result.ElapsedMilliseconds);

        return result;
    }

    private async Task FlushAsync(List<Track> batch, CancellationToken cancellationToken)
    {
        await _dbContext.Tracks.AddRangeAsync(batch, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        // Descarta as entidades rastreadas (e os domain events pendentes, nunca despachados) para o pico de
        // memória ficar preso ao tamanho do lote, e não ao do catálogo.
        _dbContext.ChangeTracker.Clear();
        batch.Clear();
    }

    /// <summary>
    /// Constrói a faixa a partir da linha, ou devolve <see langword="false"/> quando a linha é incompleta
    /// (sem alguma grandeza numérica, popularity fora de 0–100, ou id/nome vazio). Estático e sem I/O — o
    /// coração testável do seeder.
    /// </summary>
    internal static bool TryCreateTrack(KaggleCatalogRow row, out Track? track)
    {
        track = null;

        if (string.IsNullOrWhiteSpace(row.TrackId) || string.IsNullOrWhiteSpace(row.TrackName))
            return false;

        if (row.Popularity is not { } popularityValue || popularityValue < Popularity.Min || popularityValue > Popularity.Max)
            return false;

        if (row.DurationMs is not { } durationMs || durationMs < 0)
            return false;

        if (row.Danceability is not { } danceability
            || row.Energy is not { } energy
            || row.Valence is not { } valence
            || row.Tempo is not { } tempo
            || row.Acousticness is not { } acousticness
            || row.Instrumentalness is not { } instrumentalness
            || row.Liveness is not { } liveness
            || row.Speechiness is not { } speechiness
            || row.Loudness is not { } loudness
            || row.Key is not { } key
            || row.Mode is not { } mode
            || row.TimeSignature is not { } timeSignature)
        {
            return false;
        }

        string name = row.TrackName.Length > MaxTrackNameLength
            ? row.TrackName[..MaxTrackNameLength]
            : row.TrackName;

        Track built = Track.Register(
            SpotifyTrackId.Of(row.TrackId),
            name,
            Popularity.Of(popularityValue),
            durationMs,
            row.Explicit,
            albumId: null,
            artists: Array.Empty<TrackArtist>(),
            isrc: null);

        built.AttachAudioFeatures(AudioFeatures.Create(
            danceability: danceability,
            energy: energy,
            valence: valence,
            tempo: tempo,
            acousticness: acousticness,
            instrumentalness: instrumentalness,
            liveness: liveness,
            speechiness: speechiness,
            loudness: loudness,
            key: key,
            mode: mode,
            timeSignature: timeSignature,
            source: SourceName,
            genre: row.Genre,
            isImputed: false));

        track = built;
        return true;
    }
}
