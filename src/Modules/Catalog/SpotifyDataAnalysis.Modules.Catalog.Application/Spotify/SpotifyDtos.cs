namespace SpotifyDataAnalysis.Modules.Catalog.Application.Spotify;

/// <summary>DTO interno de uma faixa retornada pela Spotify Web API (achatado para consumo do módulo).</summary>
public sealed record SpotifyTrack(
    string Id,
    string Name,
    int Popularity,
    int DurationMs,
    bool Explicit,
    IReadOnlyList<SpotifyArtistRef> Artists,
    SpotifyAlbumRef? Album);

/// <summary>Referência enxuta a um artista dentro de uma faixa/álbum.</summary>
public sealed record SpotifyArtistRef(string Id, string Name);

/// <summary>Referência enxuta a um álbum dentro de uma faixa.</summary>
public sealed record SpotifyAlbumRef(string Id, string Name);

/// <summary>DTO interno de um artista.</summary>
public sealed record SpotifyArtist(
    string Id,
    string Name,
    int Popularity,
    int Followers,
    IReadOnlyList<string> Genres);

/// <summary>DTO interno de um álbum.</summary>
public sealed record SpotifyAlbum(
    string Id,
    string Name,
    string? ReleaseDate,
    int TotalTracks);

/// <summary>
/// Página de faixas de uma playlist. Espelha o envelope de paginação da Spotify (offset/limit/total) e
/// deriva <see cref="HasNext"/> para o caller iterar sem reprocessar o link <c>next</c> cru.
/// </summary>
public sealed record SpotifyPlaylistTracksPage(
    IReadOnlyList<SpotifyTrack> Items,
    int Offset,
    int Limit,
    int Total)
{
    /// <summary>Há mais páginas depois desta (offset + itens retornados &lt; total).</summary>
    public bool HasNext => Offset + Items.Count < Total;
}
