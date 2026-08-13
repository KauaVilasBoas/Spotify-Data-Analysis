using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Playlists;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;
using SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Persistence;

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Seeding;

/// <summary>Censo de uma execução do seed de playlists do Pichl — os números que alimentam os gates (a) e (b).</summary>
/// <param name="RowsRead">Linhas do Pichl consumidas (pares playlist-faixa).</param>
/// <param name="RowsMatched">Linhas cuja faixa casou com um <c>track_id</c> do catálogo.</param>
/// <param name="RowsUnmatched">Linhas cuja faixa NÃO casou (não está no catálogo, ou chave ambígua/vazia).</param>
/// <param name="PlaylistsSeen">Playlists distintas (por <c>user_id</c>+<c>playlistname</c>) consideradas.</param>
/// <param name="PlaylistsCreated">Playlists efetivamente criadas nesta execução.</param>
/// <param name="PlaylistsSkippedExisting">Playlists puladas por já terem sido semeadas (idempotência).</param>
/// <param name="PlaylistsSkippedEmpty">Playlists descartadas por nenhuma faixa ter casado com o catálogo.</param>
/// <param name="DistinctCatalogTracksCovered">Faixas distintas do catálogo que entraram em ao menos uma playlist.</param>
/// <param name="ResolvableMatchKeys">Chaves "artista|título" únicas do mapa de reconciliação.</param>
/// <param name="AmbiguousMatchKeysDiscarded">Chaves descartadas por apontar para track_ids distintos.</param>
/// <param name="ElapsedMilliseconds">Duração total da carga.</param>
public sealed record PichlPlaylistSeedResult(
    long RowsRead,
    long RowsMatched,
    long RowsUnmatched,
    long PlaylistsSeen,
    long PlaylistsCreated,
    long PlaylistsSkippedExisting,
    long PlaylistsSkippedEmpty,
    long DistinctCatalogTracksCovered,
    int ResolvableMatchKeys,
    int AmbiguousMatchKeysDiscarded,
    long ElapsedMilliseconds)
{
    /// <summary>Gate (a): fração das linhas do Pichl que casaram com o catálogo. Zero quando nada foi lido.</summary>
    public double MatchRate => RowsRead == 0 ? 0.0 : (double)RowsMatched / RowsRead;
}

/// <summary>
/// Seed de <c>catalog.playlists</c> a partir do dataset "Spotify Playlists" (Pichl et al.) — E4.5. Dá ao projeto
/// o insumo de <b>co-ocorrência item-item</b> que o content-based (E4.1–E4.4) não tem: o <c>dataset.csv</c> é
/// uma linha por faixa e não agrupa playlists, então sem esta carga a matriz de co-ocorrência do E4.6 é vazia.
///
/// <para><b>A ponte é textual, não por id</b> (a pega central do card): o Pichl identifica faixa por
/// <c>trackname</c>+<c>artistname</c>, o catálogo por <c>track_id</c>. O <see cref="CatalogMatchKeyIndex"/>
/// reconstrói o mapa <c>"artista|título" → track_id</c> a partir do <c>dataset.csv</c> (o único lugar com o nome
/// do artista) e é ele quem resolve cada faixa do Pichl. Só as faixas que casam entram na playlist.</para>
///
/// <para><b>Escreve pelo <see cref="CatalogDbContext"/> cru, em lotes</b> (<c>AddRange</c> + <c>SaveChanges</c> +
/// <c>ChangeTracker.Clear</c>), NÃO pelo <c>EfUnitOfWork</c> — exatamente como o <see cref="KaggleCatalogSeeder"/>.
/// É o que garante que o <see cref="Domain.Playlists.Events.PlaylistIngestedDomainEvent"/> levantado por
/// <see cref="Playlist.RecordIngestion"/> jamais chegue ao Outbox: o dispatch só ocorre no UnitOfWork, e o
/// <c>ChangeTracker.Clear</c> descarta os eventos pendentes de cada lote. Semear dezenas de milhares de playlists
/// não pode despejar dezenas de milhares de integration events.</para>
///
/// <para><b>Volume com gate (DP-3):</b> a fonte tem ~12,9M linhas; o seeder lê em streaming e para de ABRIR novas
/// playlists ao atingir <paramref name="maxPlaylists"/>. Playlists já abertas continuam recebendo faixas até o
/// fim do arquivo, para não gravar uma playlist truncada.</para>
/// </summary>
public sealed class PichlPlaylistSeeder
{
    /// <summary>Momento fixo carimbado como a "ingestão" de todas as playlists desta carga — é seed, não coleta real.</summary>
    private static readonly DateTime SeedIngestedAtUtc = new(2015, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Teto do nome da playlist = a coluna <c>name varchar(400)</c>. Nomes maiores são cortados, não descartados.</summary>
    private const int MaxPlaylistNameLength = 400;

    private readonly CatalogDbContext _dbContext;
    private readonly ILogger<PichlPlaylistSeeder> _logger;

    public PichlPlaylistSeeder(CatalogDbContext dbContext, ILogger<PichlPlaylistSeeder> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Semeia as playlists do Pichl casadas ao catálogo.
    /// </summary>
    /// <param name="pichlCsvFilePath">Caminho do CSV do Pichl (<c>spotify_dataset.csv</c>).</param>
    /// <param name="catalogDatasetCsvPath">Caminho do <c>dataset.csv</c> original, para reconstruir o mapa de casamento.</param>
    /// <param name="maxPlaylists">Teto de playlists NOVAS a abrir nesta execução (DP-3). Não-positivo = sem teto.</param>
    /// <param name="batchSize">Playlists por lote de escrita.</param>
    public async Task<PichlPlaylistSeedResult> SeedAsync(
        string pichlCsvFilePath,
        string catalogDatasetCsvPath,
        int maxPlaylists = 50_000,
        int batchSize = 1_000,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(pichlCsvFilePath))
            throw new FileNotFoundException($"CSV do Pichl não encontrado: {pichlCsvFilePath}", pichlCsvFilePath);

        Stopwatch stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Reconstruindo o mapa de casamento a partir de {Dataset}...", catalogDatasetCsvPath);
        CatalogMatchKeyIndex matchIndex =
            await CatalogMatchKeyIndex.BuildFromCatalogDatasetAsync(catalogDatasetCsvPath, cancellationToken);
        _logger.LogInformation(
            "Mapa de casamento: {Resolvable} chaves resolvíveis, {Ambiguous} ambíguas descartadas.",
            matchIndex.ResolvableKeys, matchIndex.AmbiguousKeysDiscarded);

        // Resume/idempotência: os ids de playlist já semeados. O id é derivado (user_id+playlistname), então
        // reexecutar sobre o mesmo CSV reencontra os mesmos ids e os pula.
        var existingPlaylistIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (SpotifyPlaylistId id in
                 await _dbContext.Playlists.AsNoTracking().Select(playlist => playlist.Id)
                     .ToListAsync(cancellationToken))
        {
            existingPlaylistIds.Add(id.Value);
        }

        var accumulator = new PlaylistAccumulator(maxPlaylists);
        var distinctCatalogTracks = new HashSet<string>(StringComparer.Ordinal);
        long rowsRead = 0, rowsMatched = 0, rowsUnmatched = 0;

        await foreach (PichlPlaylistRow row in
            PichlPlaylistCsvReader.ReadAsync(pichlCsvFilePath, cancellationToken))
        {
            rowsRead++;

            string playlistId = CsvDerivedIdentity.From(row.UserId, row.PlaylistName);

            // Playlist já no banco: nem tenta acumular (idempotência + economia de memória).
            if (existingPlaylistIds.Contains(playlistId))
                continue;

            string? trackId = matchIndex.ResolveTrackId(row.TrackName, row.ArtistName);
            if (trackId is null)
            {
                rowsUnmatched++;
                // Ainda registramos a playlist como "vista", mas sem faixa — para o teto de playlists e o censo
                // refletirem o que o arquivo trouxe, mesmo quando nada dela casou.
                accumulator.TouchPlaylist(playlistId, row.PlaylistName);
                continue;
            }

            rowsMatched++;
            distinctCatalogTracks.Add(trackId);
            accumulator.AddTrack(playlistId, row.PlaylistName, trackId);
        }

        (long created, long skippedEmpty) = await PersistAsync(accumulator, batchSize, cancellationToken);

        stopwatch.Stop();

        long playlistsSeen = accumulator.PlaylistsSeen;
        long skippedEmptyTotal = skippedEmpty;

        var result = new PichlPlaylistSeedResult(
            RowsRead: rowsRead,
            RowsMatched: rowsMatched,
            RowsUnmatched: rowsUnmatched,
            PlaylistsSeen: playlistsSeen,
            PlaylistsCreated: created,
            PlaylistsSkippedExisting: existingPlaylistIds.Count,
            PlaylistsSkippedEmpty: skippedEmptyTotal,
            DistinctCatalogTracksCovered: distinctCatalogTracks.Count,
            ResolvableMatchKeys: matchIndex.ResolvableKeys,
            AmbiguousMatchKeysDiscarded: matchIndex.AmbiguousKeysDiscarded,
            ElapsedMilliseconds: (long)stopwatch.Elapsed.TotalMilliseconds);

        _logger.LogInformation(
            "Seed de playlists (Pichl): {Created} playlists criadas ({SkippedEmpty} sem casamento, " +
            "{SkippedExisting} já existentes) de {Seen} vistas; {Matched}/{Read} linhas casadas " +
            "(taxa {Rate:P2}); {Tracks} faixas distintas do catálogo cobertas em {ElapsedMs} ms.",
            result.PlaylistsCreated, result.PlaylistsSkippedEmpty, result.PlaylistsSkippedExisting,
            result.PlaylistsSeen, result.RowsMatched, result.RowsRead, result.MatchRate,
            result.DistinctCatalogTracksCovered, result.ElapsedMilliseconds);

        return result;
    }

    /// <summary>
    /// Materializa as playlists acumuladas em agregados e as grava em lotes. Playlists sem NENHUMA faixa casada
    /// são descartadas (contadas): uma playlist vazia não contribui para a co-ocorrência e só ocuparia espaço.
    /// </summary>
    private async Task<(long Created, long SkippedEmpty)> PersistAsync(
        PlaylistAccumulator accumulator, int batchSize, CancellationToken cancellationToken)
    {
        var batch = new List<Playlist>(batchSize);
        long created = 0, skippedEmpty = 0;

        foreach (AccumulatedPlaylist pending in accumulator.Drain())
        {
            if (pending.TrackIds.Count == 0)
            {
                skippedEmpty++;
                continue;
            }

            Playlist playlist = Playlist.RegisterAsSeed(
                SpotifyPlaylistId.Of(pending.PlaylistId), Truncate(pending.Name), ownerDisplayName: null);

            playlist.RecordIngestion(
                pending.TrackIds.Select(SpotifyTrackId.Of), SeedIngestedAtUtc);

            batch.Add(playlist);
            created++;

            if (batch.Count >= batchSize)
                await FlushAsync(batch, cancellationToken);
        }

        if (batch.Count > 0)
            await FlushAsync(batch, cancellationToken);

        return (created, skippedEmpty);
    }

    private async Task FlushAsync(List<Playlist> batch, CancellationToken cancellationToken)
    {
        await _dbContext.Playlists.AddRangeAsync(batch, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        // Descarta as entidades rastreadas E os domain events pendentes (nunca despachados) para o pico de
        // memória ficar preso ao tamanho do lote — e para o Outbox nunca ver um PlaylistIngestedDomainEvent.
        _dbContext.ChangeTracker.Clear();
        batch.Clear();
    }

    private static string Truncate(string value) =>
        value.Length > MaxPlaylistNameLength ? value[..MaxPlaylistNameLength] : value;
}
