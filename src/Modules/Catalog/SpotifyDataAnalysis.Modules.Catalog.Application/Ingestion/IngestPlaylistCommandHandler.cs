using SpotifyDataAnalysis.Modules.Catalog.Application.Spotify;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;

/// <summary>
/// Handler de <see cref="IngestPlaylistCommand"/>: percorre (paginado/streaming) as faixas da playlist e,
/// para cada uma, registra um novo <see cref="Track"/> ou atualiza a popularidade do existente
/// (idempotência por <c>SpotifyTrackId</c>). Faixas sem id/nome (ex.: arquivos locais, itens removidos)
/// são puladas. A persistência + Outbox acontecem no commit do command (UnitOfWork/TransactionBehavior).
/// </summary>
internal sealed class IngestPlaylistCommandHandler : ICommandHandler<IngestPlaylistCommand, IngestPlaylistResult>
{
    private const int PageSize = 100;

    private readonly ISpotifyClient _spotify;
    private readonly ITrackRepository _tracks;

    public IngestPlaylistCommandHandler(ISpotifyClient spotify, ITrackRepository tracks)
    {
        _spotify = spotify;
        _tracks = tracks;
    }

    public async Task<IngestPlaylistResult> HandleAsync(
        IngestPlaylistCommand request, CancellationToken cancellationToken = default)
    {
        int ingested = 0, updated = 0, skipped = 0, total = 0;

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

            SpotifyTrackId id = SpotifyTrackId.Of(dto.Id);
            Track? existing = await _tracks.GetByIdAsync(id, cancellationToken);

            if (existing is null)
            {
                Track track = Track.Register(
                    id, dto.Name, Popularity.Of(dto.Popularity), dto.DurationMs, dto.Explicit,
                    dto.Album?.Id, dto.Artists.Select(artist => artist.Id));

                await _tracks.AddAsync(track, cancellationToken);
                ingested++;
            }
            else
            {
                // A popularidade varia com o tempo — reingestão atualiza o valor.
                existing.UpdatePopularity(Popularity.Of(dto.Popularity));
                updated++;
            }
        }

        return new IngestPlaylistResult(ingested, updated, skipped, total);
    }
}
