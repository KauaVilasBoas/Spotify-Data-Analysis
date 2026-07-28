using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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

    /// <inheritdoc />
    public async Task<SpotifyArtist?> GetArtistAsync(string artistId, CancellationToken cancellationToken = default)
    {
        ArtistJson? json = await GetAsync<ArtistJson>($"artists/{artistId}", cancellationToken);
        if (json is null) return null;

        return new SpotifyArtist(
            json.Id ?? string.Empty,
            json.Name ?? string.Empty,
            json.Popularity,
            json.Followers?.Total ?? 0,
            json.Genres ?? []);
    }

    /// <inheritdoc />
    public async Task<SpotifyAlbum?> GetAlbumAsync(string albumId, CancellationToken cancellationToken = default)
    {
        AlbumJson? json = await GetAsync<AlbumJson>($"albums/{albumId}", cancellationToken);
        if (json is null) return null;

        return new SpotifyAlbum(
            json.Id ?? string.Empty,
            json.Name ?? string.Empty,
            json.ReleaseDate,
            json.TotalTracks);
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
            album);
    }

    // ---- Modelos de desserialização do JSON externo (mantidos privados ao adapter) ----

    private sealed record TrackJson(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("popularity")] int Popularity,
        [property: JsonPropertyName("duration_ms")] int DurationMs,
        [property: JsonPropertyName("explicit")] bool Explicit,
        [property: JsonPropertyName("artists")] List<ArtistRefJson>? Artists,
        [property: JsonPropertyName("album")] AlbumRefJson? Album);

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

    private sealed record PlaylistTracksPageJson(
        [property: JsonPropertyName("items")] List<PlaylistItemJson>? Items,
        [property: JsonPropertyName("offset")] int Offset,
        [property: JsonPropertyName("limit")] int Limit,
        [property: JsonPropertyName("total")] int Total);

    private sealed record PlaylistItemJson(
        [property: JsonPropertyName("track")] TrackJson? Track);
}
