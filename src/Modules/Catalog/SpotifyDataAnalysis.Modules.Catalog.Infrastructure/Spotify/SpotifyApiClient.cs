using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpotifyDataAnalysis.Modules.Catalog.Application.Spotify;

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Spotify;

/// <summary>
/// Adapter HTTP tipado para a <b>Spotify Web API</b> (implementa <see cref="ISpotifyClient"/>).
///
/// Registrado via <c>AddHttpClient&lt;ISpotifyClient, SpotifyApiClient&gt;</c> (BaseAddress da configuração).
/// Cada requisição obtém um Bearer token via <see cref="ISpotifyTokenProvider"/> e mapeia o JSON externo
/// para os DTOs internos do módulo (anti-corruption) — o Domain nunca vê o contrato da API.
///
/// <para><b>Fora de escopo neste card:</b> resiliência (retry/backoff, tratamento de <c>429</c>/Retry-After)
/// é o card <b>E0.4</b> — aqui um erro HTTP não-2xx (exceto 404, tratado como "não encontrado") sobe como
/// exceção via <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/>.</para>
/// </summary>
public sealed class SpotifyApiClient : ISpotifyClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly ISpotifyTokenProvider _tokenProvider;

    public SpotifyApiClient(HttpClient http, ISpotifyTokenProvider tokenProvider)
    {
        _http = http;
        _tokenProvider = tokenProvider;
    }

    /// <inheritdoc />
    public async Task<SpotifyTrack?> GetTrackAsync(string trackId, CancellationToken cancellationToken = default)
    {
        TrackJson? json = await GetAsync<TrackJson>($"tracks/{trackId}", cancellationToken);
        return json is null ? null : MapTrack(json);
    }

    /// <summary>Ids por chamada dos endpoints em lote — os tetos que a Spotify Web API impõe.</summary>
    private const int MaxArtistsPerRequest = 50;
    private const int MaxAlbumsPerRequest = 20;

    /// <inheritdoc />
    public async Task<SpotifyArtist?> GetArtistAsync(string artistId, CancellationToken cancellationToken = default)
    {
        ArtistJson? json = await GetAsync<ArtistJson>($"artists/{artistId}", cancellationToken);
        return json is null ? null : MapArtist(json);
    }

    /// <inheritdoc />
    public async Task<SpotifyAlbum?> GetAlbumAsync(string albumId, CancellationToken cancellationToken = default)
    {
        AlbumJson? json = await GetAsync<AlbumJson>($"albums/{albumId}", cancellationToken);
        return json is null ? null : MapAlbum(json);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SpotifyArtist>> GetArtistsAsync(
        IReadOnlyCollection<string> artistIds, CancellationToken cancellationToken = default)
    {
        var artists = new List<SpotifyArtist>(artistIds.Count);

        foreach (string[] chunk in Chunk(artistIds, MaxArtistsPerRequest))
        {
            ArtistsEnvelopeJson? envelope = await GetAsync<ArtistsEnvelopeJson>(
                $"artists?ids={string.Join(',', chunk)}", cancellationToken);

            // A API devolve `null` na posição de um id inválido/desconhecido — descartamos essas lacunas.
            artists.AddRange((envelope?.Artists ?? [])
                .Where(json => json is not null)
                .Select(json => MapArtist(json!)));
        }

        return artists;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SpotifyAlbum>> GetAlbumsAsync(
        IReadOnlyCollection<string> albumIds, CancellationToken cancellationToken = default)
    {
        var albums = new List<SpotifyAlbum>(albumIds.Count);

        foreach (string[] chunk in Chunk(albumIds, MaxAlbumsPerRequest))
        {
            AlbumsEnvelopeJson? envelope = await GetAsync<AlbumsEnvelopeJson>(
                $"albums?ids={string.Join(',', chunk)}", cancellationToken);

            albums.AddRange((envelope?.Albums ?? [])
                .Where(json => json is not null)
                .Select(json => MapAlbum(json!)));
        }

        return albums;
    }

    private static IEnumerable<string[]> Chunk(IReadOnlyCollection<string> ids, int size)
        => ids.Where(id => !string.IsNullOrWhiteSpace(id)).Chunk(size);

    private static SpotifyArtist MapArtist(ArtistJson json)
        => new(
            json.Id ?? string.Empty,
            json.Name ?? string.Empty,
            json.Popularity,
            json.Followers?.Total ?? 0,
            json.Genres ?? []);

    private static SpotifyAlbum MapAlbum(AlbumJson json)
        => new(
            json.Id ?? string.Empty,
            json.Name ?? string.Empty,
            json.ReleaseDate,
            json.TotalTracks);

    /// <inheritdoc />
    public async Task<SpotifyPlaylist?> GetPlaylistAsync(
        string playlistId, CancellationToken cancellationToken = default)
    {
        // "fields" enxuga a resposta: sem isso a API devolve a primeira página de faixas junto (payload
        // muito maior) — as faixas são coletadas à parte por GetPlaylistTracksAsync.
        PlaylistJson? json = await GetAsync<PlaylistJson>(
            $"playlists/{playlistId}?fields=id,name,owner(display_name),tracks(total)", cancellationToken);

        if (json is null) return null;

        return new SpotifyPlaylist(
            json.Id ?? string.Empty,
            json.Name ?? string.Empty,
            json.Owner?.DisplayName,
            json.Tracks?.Total ?? 0);
    }

    /// <inheritdoc />
    public async Task<SpotifyPlaylistTracksPage> GetPlaylistTracksAsync(
        string playlistId, int offset, int limit, CancellationToken cancellationToken = default)
    {
        PlaylistTracksPageJson? json = await GetAsync<PlaylistTracksPageJson>(
            $"playlists/{playlistId}/tracks?offset={offset}&limit={limit}", cancellationToken);

        if (json is null)
            return new SpotifyPlaylistTracksPage([], offset, limit, Total: 0);

        List<SpotifyTrack> tracks = (json.Items ?? [])
            .Where(item => item.Track is not null)
            .Select(item => MapTrack(item.Track!))
            .ToList();

        return new SpotifyPlaylistTracksPage(tracks, json.Offset, json.Limit, json.Total);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SpotifyTrack> StreamPlaylistTracksAsync(
        string playlistId, int pageSize = 100,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        int offset = 0;
        while (true)
        {
            SpotifyPlaylistTracksPage page =
                await GetPlaylistTracksAsync(playlistId, offset, pageSize, cancellationToken);

            foreach (SpotifyTrack track in page.Items)
                yield return track;

            // Termina quando não há próxima página (ou a página veio vazia, evitando laço infinito).
            if (!page.HasNext || page.Items.Count == 0)
                yield break;

            offset += page.Items.Count;
        }
    }

    private async Task<T?> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        string token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);

        // Recurso inexistente é um resultado esperado (não um erro): o caller trata null.
        if (response.StatusCode == HttpStatusCode.NotFound)
            return default;

        // Resiliência (retry/backoff, 429/Retry-After) é o E0.4 — por ora, não-2xx sobe como exceção.
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    private static SpotifyTrack MapTrack(TrackJson json)
    {
        IReadOnlyList<SpotifyArtistRef> artists = (json.Artists ?? [])
            .Select(a => new SpotifyArtistRef(a.Id ?? string.Empty, a.Name ?? string.Empty))
            .ToList();

        SpotifyAlbumRef? album = json.Album is { Id: not null }
            ? new SpotifyAlbumRef(json.Album.Id, json.Album.Name ?? string.Empty)
            : null;

        return new SpotifyTrack(
            json.Id ?? string.Empty,
            json.Name ?? string.Empty,
            json.Popularity,
            json.DurationMs,
            json.Explicit,
            artists,
            album,
            json.ExternalIds?.Isrc);
    }

    // ---- Modelos de desserialização do JSON externo (mantidos privados ao adapter) ----

    private sealed record TrackJson(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("popularity")] int Popularity,
        [property: JsonPropertyName("duration_ms")] int DurationMs,
        [property: JsonPropertyName("explicit")] bool Explicit,
        [property: JsonPropertyName("artists")] List<ArtistRefJson>? Artists,
        [property: JsonPropertyName("album")] AlbumRefJson? Album,
        [property: JsonPropertyName("external_ids")] ExternalIdsJson? ExternalIds);

    /// <summary>
    /// Identificadores da faixa em catálogos externos ao Spotify. Só o <c>isrc</c> interessa ao módulo — os
    /// demais (<c>ean</c>, <c>upc</c>) identificam o produto comercial, não a gravação.
    ///
    /// <para>O objeto vem no <b>full track object</b> (endpoints de faixa e de itens de playlist, que este
    /// adapter consome sem filtro <c>fields</c>) e é omitido nos objetos simplificados. Ausente ⇒ nulo.</para>
    /// </summary>
    private sealed record ExternalIdsJson(
        [property: JsonPropertyName("isrc")] string? Isrc);

    private sealed record ArtistRefJson(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name);

    private sealed record AlbumRefJson(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name);

    private sealed record ArtistJson(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("popularity")] int Popularity,
        [property: JsonPropertyName("followers")] FollowersJson? Followers,
        [property: JsonPropertyName("genres")] List<string>? Genres);

    private sealed record FollowersJson(
        [property: JsonPropertyName("total")] int Total);

    private sealed record AlbumJson(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("release_date")] string? ReleaseDate,
        [property: JsonPropertyName("total_tracks")] int TotalTracks);

    /// <summary>Envelope dos endpoints em lote: a lista pode conter <c>null</c> nas posições de ids inválidos.</summary>
    private sealed record ArtistsEnvelopeJson(
        [property: JsonPropertyName("artists")] List<ArtistJson?>? Artists);

    private sealed record AlbumsEnvelopeJson(
        [property: JsonPropertyName("albums")] List<AlbumJson?>? Albums);

    private sealed record PlaylistJson(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("owner")] PlaylistOwnerJson? Owner,
        [property: JsonPropertyName("tracks")] PlaylistTracksSummaryJson? Tracks);

    private sealed record PlaylistOwnerJson(
        [property: JsonPropertyName("display_name")] string? DisplayName);

    private sealed record PlaylistTracksSummaryJson(
        [property: JsonPropertyName("total")] int Total);

    private sealed record PlaylistTracksPageJson(
        [property: JsonPropertyName("items")] List<PlaylistItemJson>? Items,
        [property: JsonPropertyName("offset")] int Offset,
        [property: JsonPropertyName("limit")] int Limit,
        [property: JsonPropertyName("total")] int Total);

    private sealed record PlaylistItemJson(
        [property: JsonPropertyName("track")] TrackJson? Track);
}
