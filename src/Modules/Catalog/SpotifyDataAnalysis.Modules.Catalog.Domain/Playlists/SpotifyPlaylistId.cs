using SpotifyDataAnalysis.Modules.Catalog.Domain.Common;

namespace SpotifyDataAnalysis.Modules.Catalog.Domain.Playlists;

/// <summary>Identificador de uma playlist no Spotify — identidade do agregado <see cref="Playlist"/>.</summary>
public sealed class SpotifyPlaylistId : SpotifyResourceId
{
    private SpotifyPlaylistId(string value) : base(value) { }

    public static SpotifyPlaylistId Of(string value) => new(Normalize(value, nameof(value)));
}
