using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;

namespace SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion.Matching;

/// <summary>
/// Fallback: casa a linha pela chave normalizada "artista principal + título" quando o <c>track_id</c> não
/// existe no catálogo (ids divergem entre snapshots da API e do dataset — um dos riscos mapeados do projeto).
///
/// Recusa-se a agir com chave vazia: sem título nem artista utilizáveis, qualquer casamento seria arbitrário.
/// </summary>
internal sealed class NameAndArtistMatchingStrategy : ITrackMatchingStrategy
{
    private readonly ITrackRepository _tracks;

    public NameAndArtistMatchingStrategy(ITrackRepository tracks) => _tracks = tracks;

    public TrackMatchKind Kind => TrackMatchKind.NameAndArtist;

    public async Task<Track?> MatchAsync(
        KaggleAudioFeaturesRow row, CancellationToken cancellationToken = default)
    {
        TrackMatchKey matchKey = TrackMatchKey.FromArtistList(row.TrackName, row.Artists);

        if (matchKey.IsEmpty)
            return null;

        return await _tracks.FindByMatchKeyAsync(matchKey, cancellationToken);
    }
}
