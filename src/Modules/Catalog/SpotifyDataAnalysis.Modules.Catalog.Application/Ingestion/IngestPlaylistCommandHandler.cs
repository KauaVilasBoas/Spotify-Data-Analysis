using SpotifyDataAnalysis.Modules.Catalog.Application.Spotify;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Common;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Playlists;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;
using SpotifyDataAnalysis.SharedKernel.Exceptions;
using SpotifyDataAnalysis.SharedKernel.Messaging;
using SpotifyDataAnalysis.SharedKernel.Time;

namespace SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;

/// <summary>
/// Handler de <see cref="IngestPlaylistCommand"/>. Orquestra um ciclo de coleta:
///
/// <list type="number">
///   <item>registra (ou atualiza) a <see cref="Playlist"/> semente;</item>
///   <item>percorre as faixas em streaming paginado e, para cada uma, garante artistas/álbum no catálogo
///         (via <see cref="CatalogReferenceRegistrar"/>) e registra a <see cref="Track"/> nova ou reaplica o
///         retrato mais recente sobre a existente;</item>
///   <item>fecha o ciclo em <see cref="Playlist.RecordIngestion"/>, que carimba a data e emite o domain event.</item>
/// </list>
///
/// A persistência + Outbox acontecem no commit do command (UnitOfWork/TransactionBehavior) — ou seja, um
/// ciclo de coleta é <b>tudo ou nada</b>: nunca sobra playlist carimbada sem as faixas correspondentes.
/// </summary>
internal sealed class IngestPlaylistCommandHandler : ICommandHandler<IngestPlaylistCommand, IngestPlaylistResult>
{
    private const int PageSize = 100;

    private readonly ISpotifyClient _spotify;
    private readonly ITrackRepository _tracks;
    private readonly IPlaylistRepository _playlists;
    private readonly CatalogReferenceRegistrar _references;
    private readonly IClock _clock;

    public IngestPlaylistCommandHandler(
        ISpotifyClient spotify,
        ITrackRepository tracks,
        IPlaylistRepository playlists,
        CatalogReferenceRegistrar references,
        IClock clock)
    {
        _spotify = spotify;
        _tracks = tracks;
        _playlists = playlists;
        _references = references;
        _clock = clock;
    }

    public async Task<IngestPlaylistResult> HandleAsync(
        IngestPlaylistCommand request, CancellationToken cancellationToken = default)
    {
        Playlist playlist = await RegisterOrUpdateSeedAsync(request.SpotifyPlaylistId, cancellationToken);

        // Uma mesma faixa pode aparecer duas vezes na playlist — o conjunto abaixo mantém a contagem honesta
        // e evita reprocessar o agregado.
        var seenTrackIds = new HashSet<string>(StringComparer.Ordinal);
        var collectedTrackIds = new List<SpotifyTrackId>();

        int ingested = 0, updated = 0, skipped = 0, duplicates = 0, total = 0;

        await foreach (SpotifyTrack dto in _spotify.StreamPlaylistTracksAsync(
            request.SpotifyPlaylistId, PageSize, cancellationToken))
        {
            total++;

            // Faixas sem id/nome não são registráveis (arquivos locais, itens removidos da playlist).
            if (string.IsNullOrWhiteSpace(dto.Id) || string.IsNullOrWhiteSpace(dto.Name))
            {
                skipped++;
                continue;
            }

            if (!seenTrackIds.Add(dto.Id))
            {
                duplicates++;
                continue;
            }

            SpotifyTrackId id = SpotifyTrackId.Of(dto.Id);
            collectedTrackIds.Add(id);

            await _references.EnsureArtistsRegisteredAsync(dto.Artists, cancellationToken);
            await _references.EnsureAlbumRegisteredAsync(dto.Album, cancellationToken);

            if (await CatalogTrackAsync(id, dto, cancellationToken))
                ingested++;
            else
                updated++;
        }

        playlist.RecordIngestion(collectedTrackIds, _clock.UtcNow);

        return new IngestPlaylistResult(ingested, updated, skipped, duplicates, total);
    }

    /// <summary>
    /// Carrega a playlist-semente do catálogo, registrando-a na primeira coleta e sincronizando os metadados
    /// nas seguintes.
    /// </summary>
    private async Task<Playlist> RegisterOrUpdateSeedAsync(
        string spotifyPlaylistId, CancellationToken cancellationToken)
    {
        SpotifyPlaylist? metadata = await _spotify.GetPlaylistAsync(spotifyPlaylistId, cancellationToken)
            ?? throw new NotFoundException($"Playlist '{spotifyPlaylistId}' não encontrada no Spotify.");

        SpotifyPlaylistId id = SpotifyPlaylistId.Of(spotifyPlaylistId);
        Playlist? existing = await _playlists.GetByIdAsync(id, cancellationToken);

        // Playlists sem nome na API (raro) recebem o próprio id como rótulo — o agregado exige nome.
        string name = string.IsNullOrWhiteSpace(metadata.Name) ? spotifyPlaylistId : metadata.Name;

        if (existing is null)
        {
            Playlist playlist = Playlist.RegisterAsSeed(id, name, metadata.OwnerDisplayName);
            await _playlists.AddAsync(playlist, cancellationToken);
            return playlist;
        }

        existing.UpdateMetadata(name, metadata.OwnerDisplayName);
        return existing;
    }

    /// <summary>
    /// Registra a faixa ou reaplica sobre a existente o retrato vindo da API.
    /// Devolve <see langword="true"/> quando a faixa era nova no catálogo.
    /// </summary>
    private async Task<bool> CatalogTrackAsync(
        SpotifyTrackId id, SpotifyTrack dto, CancellationToken cancellationToken)
    {
        List<TrackArtist> artists = dto.Artists
            .Where(artist => !string.IsNullOrWhiteSpace(artist.Id) && !string.IsNullOrWhiteSpace(artist.Name))
            .Select(artist => TrackArtist.Of(artist.Id, artist.Name))
            .ToList();

        Popularity popularity = Popularity.Of(dto.Popularity);
        Track? existing = await _tracks.GetByIdAsync(id, cancellationToken);

        if (existing is null)
        {
            Track track = Track.Register(
                id, dto.Name, popularity, dto.DurationMs, dto.Explicit, dto.Album?.Id, artists);

            await _tracks.AddAsync(track, cancellationToken);
            return true;
        }

        existing.RefreshFromSource(
            dto.Name, popularity, dto.DurationMs, dto.Explicit, dto.Album?.Id, artists);

        return false;
    }
}
