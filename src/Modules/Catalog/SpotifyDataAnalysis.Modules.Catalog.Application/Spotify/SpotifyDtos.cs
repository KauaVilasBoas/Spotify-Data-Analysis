namespace SpotifyDataAnalysis.Modules.Catalog.Application.Spotify;

/// <summary>
/// DTO interno de uma faixa retornada pela Spotify Web API (achatado para consumo do módulo).
///
/// <para><paramref name="Isrc"/> chega como <b>string crua</b>, e não como o value object do domínio: este é
/// o contrato de fronteira com o mundo externo, onde o valor ainda não foi validado. A conversão para
/// <c>Isrc</c> acontece na Application, que trata um código ausente ou malformado como "sem ISRC".</para>
/// </summary>
public sealed record SpotifyTrack(
    string Id,
    string Name,
    int Popularity,
    int DurationMs,
    bool Explicit,
    IReadOnlyList<SpotifyArtistRef> Artists,
    SpotifyAlbumRef? Album,
    string? Isrc);

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
/// DTO interno de uma playlist (os metadados, sem as faixas — estas vêm paginadas à parte). É o que a
/// ingestão usa para registrar/atualizar a playlist-semente no catálogo.
/// </summary>
public sealed record SpotifyPlaylist(
    string Id,
    string Name,
    string? OwnerDisplayName,
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
