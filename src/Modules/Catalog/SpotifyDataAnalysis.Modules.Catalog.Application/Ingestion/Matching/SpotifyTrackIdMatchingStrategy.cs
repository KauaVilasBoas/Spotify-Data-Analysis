using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;

namespace SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion.Matching;

/// <summary>
/// Casa a linha pelo <c>track_id</c> — o caminho preferencial, porque o dataset Kaggle usa exatamente o id
/// do Spotify. É um casamento exato: quando encontra, não há dúvida sobre a faixa.
/// </summary>
internal sealed class SpotifyTrackIdMatchingStrategy : ITrackMatchingStrategy
{
    private readonly ITrackRepository _tracks;

    public SpotifyTrackIdMatchingStrategy(ITrackRepository tracks) => _tracks = tracks;

    public TrackMatchKind Kind => TrackMatchKind.SpotifyTrackId;

    public async Task<Track?> MatchAsync(
        KaggleAudioFeaturesRow row, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(row.TrackId))
            return null;

        return await _tracks.GetByIdAsync(SpotifyTrackId.Of(row.TrackId), cancellationToken);
    }
}
