using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Albums;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Artists;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Common;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;
using SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Persistence;

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Seeding;

/// <summary>Censo de uma execução do seed de referências.</summary>
public sealed record CatalogReferenceSeedResult(
    long TotalRows,
    long ArtistsCreated,
    long AlbumsCreated,
    long TracksLinked,
    long TracksNotFound,
    long ElapsedMilliseconds);

/// <summary>
/// Deriva artistas e álbuns dos NOMES do dataset Kaggle e liga as faixas já semeadas pelo
/// <see cref="KaggleCatalogSeeder"/> a essas referências, preenchendo o jsonb de créditos e o
/// <c>album_id</c> — sem tocar na API do Spotify.
///
/// <para>Os ids são sintéticos e determinísticos (<see cref="CsvDerivedIdentity"/>). Artistas e álbuns nascem
/// como REFERÊNCIA, com <c>IsEnriched = false</c>: <c>Popularity</c> e <c>Followers</c> permanecem ausência de
/// dado, não zero real, porque esses números só existem na API do Spotify.</para>
///
/// <para>Duas passadas pelo CSV, de propósito: a primeira só coleta as referências distintas e a segunda liga
/// as faixas em lotes. Reler o arquivo é barato e mantém o pico de memória preso ao tamanho do lote, em vez de
/// exigir o mapa de todas as faixas na memória.</para>
/// </summary>
public sealed class KaggleReferenceSeeder
{
    /// <summary>Separador de múltiplos artistas no dataset — confirmado por inspeção (ex.: <c>Ingrid Michaelson;ZAYN</c>).</summary>
    private const char ArtistSeparator = ';';

    /// <summary>Teto das colunas <c>name varchar(400)</c> de artista e álbum.</summary>
    private const int MaxNameLength = 400;

    private readonly CatalogDbContext _dbContext;
    private readonly ILogger<KaggleReferenceSeeder> _logger;

    public KaggleReferenceSeeder(CatalogDbContext dbContext, ILogger<KaggleReferenceSeeder> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<CatalogReferenceSeedResult> SeedAsync(
        string csvFilePath, int batchSize = 2_000, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(csvFilePath))
            throw new FileNotFoundException($"CSV do Kaggle não encontrado: {csvFilePath}", csvFilePath);

        Stopwatch stopwatch = Stopwatch.StartNew();

        (Dictionary<string, string> artists, Dictionary<string, string> albums, long totalRows) =
            await CollectReferencesAsync(csvFilePath, cancellationToken);

        long artistsCreated = await PersistArtistsAsync(artists, batchSize, cancellationToken);
        long albumsCreated = await PersistAlbumsAsync(albums, batchSize, cancellationToken);
        (long linked, long notFound) = await LinkTracksAsync(csvFilePath, batchSize, cancellationToken);

        stopwatch.Stop();

        var result = new CatalogReferenceSeedResult(
            totalRows, artistsCreated, albumsCreated, linked, notFound,
            (long)stopwatch.Elapsed.TotalMilliseconds);

        _logger.LogInformation(
            "Seed de referências: {Artists} artistas e {Albums} álbuns criados, {Linked} faixas ligadas " +
            "({NotFound} não encontradas) de {Total} linhas em {ElapsedMs} ms.",
            result.ArtistsCreated, result.AlbumsCreated, result.TracksLinked, result.TracksNotFound,
            result.TotalRows, result.ElapsedMilliseconds);

        return result;
    }

    private static async Task<(Dictionary<string, string> Artists, Dictionary<string, string> Albums, long TotalRows)>
        CollectReferencesAsync(string csvFilePath, CancellationToken cancellationToken)
    {
        var artists = new Dictionary<string, string>(StringComparer.Ordinal);
        var albums = new Dictionary<string, string>(StringComparer.Ordinal);
        long totalRows = 0;

        await foreach (KaggleCatalogRow row in KaggleCatalogCsvReader.ReadAsync(csvFilePath, cancellationToken))
        {
            totalRows++;

            IReadOnlyList<string> credits = SplitArtists(row.Artists);
            if (credits.Count == 0)
                continue;

            foreach (string credit in credits)
                artists.TryAdd(CsvDerivedIdentity.From(credit), Truncate(credit));

            if (!string.IsNullOrWhiteSpace(row.AlbumName))
                albums.TryAdd(AlbumIdOf(credits[0], row.AlbumName), Truncate(row.AlbumName));
        }

        return (artists, albums, totalRows);
    }

    private async Task<long> PersistArtistsAsync(
        Dictionary<string, string> artists, int batchSize, CancellationToken cancellationToken)
    {
        HashSet<string> existing = (await _dbContext.Artists.AsNoTracking()
                .Select(artist => artist.Id).ToListAsync(cancellationToken))
            .Select(id => id.Value)
            .ToHashSet(StringComparer.Ordinal);

        var batch = new List<Artist>(batchSize);
        long created = 0;

        foreach ((string id, string name) in artists)
        {
            if (!existing.Add(id))
                continue;

            batch.Add(Artist.RegisterFromReference(SpotifyArtistId.Of(id), name));
            created++;

            if (batch.Count >= batchSize)
                await FlushAsync(batch, cancellationToken);
        }

        if (batch.Count > 0)
            await FlushAsync(batch, cancellationToken);

        return created;
    }

    private async Task<long> PersistAlbumsAsync(
        Dictionary<string, string> albums, int batchSize, CancellationToken cancellationToken)
    {
        HashSet<string> existing = (await _dbContext.Albums.AsNoTracking()
                .Select(album => album.Id).ToListAsync(cancellationToken))
            .Select(id => id.Value)
            .ToHashSet(StringComparer.Ordinal);

        var batch = new List<Album>(batchSize);
        long created = 0;

        foreach ((string id, string name) in albums)
        {
            if (!existing.Add(id))
                continue;

            batch.Add(Album.RegisterFromReference(SpotifyAlbumId.Of(id), name));
            created++;

            if (batch.Count >= batchSize)
                await FlushAsync(batch, cancellationToken);
        }

        if (batch.Count > 0)
            await FlushAsync(batch, cancellationToken);

        return created;
    }

    private async Task<(long Linked, long NotFound)> LinkTracksAsync(
        string csvFilePath, int batchSize, CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var chunk = new List<TrackLink>(batchSize);
        long linked = 0, notFound = 0;

        await foreach (KaggleCatalogRow row in KaggleCatalogCsvReader.ReadAsync(csvFilePath, cancellationToken))
        {
            if (!TryBuildLink(row, out TrackLink link) || !seen.Add(link.TrackId.Value))
                continue;

            chunk.Add(link);

            if (chunk.Count >= batchSize)
            {
                (long applied, long missing) = await ApplyLinksAsync(chunk, cancellationToken);
                linked += applied;
                notFound += missing;
            }
        }

        if (chunk.Count > 0)
        {
            (long applied, long missing) = await ApplyLinksAsync(chunk, cancellationToken);
            linked += applied;
            notFound += missing;
        }

        return (linked, notFound);
    }

    private async Task<(long Applied, long Missing)> ApplyLinksAsync(
        List<TrackLink> chunk, CancellationToken cancellationToken)
    {
        List<SpotifyTrackId> ids = chunk.Select(link => link.TrackId).ToList();

        Dictionary<string, Track> tracks = (await _dbContext.Tracks
                .Where(track => ids.Contains(track.Id))
                .ToListAsync(cancellationToken))
            .ToDictionary(track => track.Id.Value, StringComparer.Ordinal);

        long applied = 0, missing = 0;

        foreach (TrackLink link in chunk)
        {
            if (!tracks.TryGetValue(link.TrackId.Value, out Track? track))
            {
                missing++;
                continue;
            }

            // RefreshFromSource é o caminho de "reaplicar o retrato da fonte": reescreve os mesmos valores que o
            // E1.10 já gravou, agora COM créditos e álbum, e de quebra recalcula a MatchKey — que hoje está sem
            // o artista principal porque as faixas nasceram sem créditos.
            track.RefreshFromSource(
                track.Name,
                track.Popularity,
                track.DurationMs,
                track.Explicit,
                link.AlbumId,
                link.Credits,
                track.Isrc);

            applied++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.ChangeTracker.Clear();
        chunk.Clear();

        return (applied, missing);
    }

    private async Task FlushAsync<TEntity>(List<TEntity> batch, CancellationToken cancellationToken)
        where TEntity : class
    {
        await _dbContext.Set<TEntity>().AddRangeAsync(batch, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.ChangeTracker.Clear();
        batch.Clear();
    }

    /// <summary>
    /// Monta os créditos e o álbum de uma linha, ou devolve <see langword="false"/> quando a linha não tem
    /// artista nem id de faixa utilizáveis. Estático e sem I/O — o coração testável do seeder.
    /// </summary>
    internal static bool TryBuildLink(KaggleCatalogRow row, out TrackLink link)
    {
        link = default;

        if (string.IsNullOrWhiteSpace(row.TrackId))
            return false;

        IReadOnlyList<string> credits = SplitArtists(row.Artists);
        if (credits.Count == 0)
            return false;

        TrackArtist[] artists = credits
            .Select(credit => TrackArtist.Of(CsvDerivedIdentity.From(credit), Truncate(credit)))
            .ToArray();

        string? albumId = string.IsNullOrWhiteSpace(row.AlbumName)
            ? null
            : AlbumIdOf(credits[0], row.AlbumName);

        link = new TrackLink(SpotifyTrackId.Of(row.TrackId), artists, albumId);
        return true;
    }

    /// <summary>
    /// Separa os créditos preservando a ORDEM da fonte — o primeiro é o artista principal, convenção que o
    /// ranking do E2.2 e a listagem do E2.6 assumem ao ler <c>artists -&gt; 0</c>. Repetições dentro da mesma
    /// faixa são descartadas para não gerar crédito duplicado.
    /// </summary>
    internal static IReadOnlyList<string> SplitArtists(string? rawArtists)
    {
        if (string.IsNullOrWhiteSpace(rawArtists))
            return [];

        var credits = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string part in rawArtists.Split(ArtistSeparator))
        {
            string name = part.Trim();

            if (name.Length > 0 && seen.Add(CsvDerivedIdentity.Normalize(name)))
                credits.Add(name);
        }

        return credits;
    }

    /// <summary>
    /// O álbum é identificado por artista principal + nome, e não só pelo nome: títulos genéricos se repetem
    /// entre artistas diferentes no dataset, e identificar só pelo nome fundiria álbuns sem relação num único
    /// registro.
    /// </summary>
    internal static string AlbumIdOf(string primaryArtist, string albumName)
        => CsvDerivedIdentity.From(primaryArtist, albumName);

    private static string Truncate(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length > MaxNameLength ? trimmed[..MaxNameLength] : trimmed;
    }

    /// <summary>Vínculo derivado de uma linha do CSV: a faixa, seus créditos e o álbum.</summary>
    internal readonly record struct TrackLink(
        SpotifyTrackId TrackId,
        IReadOnlyList<TrackArtist> Credits,
        string? AlbumId);
}
