using SpotifyDataAnalysis.Modules.Catalog.Domain.Common;

namespace SpotifyDataAnalysis.Modules.Catalog.Domain.Albums;

/// <summary>Identificador de um álbum no Spotify — identidade do agregado <see cref="Album"/>.</summary>
public sealed class SpotifyAlbumId : SpotifyResourceId
{
    private SpotifyAlbumId(string value) : base(value) { }

    public static SpotifyAlbumId Of(string value) => new(Normalize(value, nameof(value)));
}
