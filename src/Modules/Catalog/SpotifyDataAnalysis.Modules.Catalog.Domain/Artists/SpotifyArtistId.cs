using SpotifyDataAnalysis.Modules.Catalog.Domain.Common;

namespace SpotifyDataAnalysis.Modules.Catalog.Domain.Artists;

/// <summary>Identificador de um artista no Spotify — identidade do agregado <see cref="Artist"/>.</summary>
public sealed class SpotifyArtistId : SpotifyResourceId
{
    private SpotifyArtistId(string value) : base(value) { }

    public static SpotifyArtistId Of(string value) => new(Normalize(value, nameof(value)));
}
