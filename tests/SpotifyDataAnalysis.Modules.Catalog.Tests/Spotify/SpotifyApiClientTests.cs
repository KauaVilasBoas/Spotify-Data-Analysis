using System.Net;
using System.Net.Http.Headers;
using SpotifyDataAnalysis.Modules.Catalog.Application.Spotify;
using SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Spotify;

namespace SpotifyDataAnalysis.Modules.Catalog.Tests.Spotify;

/// <summary>
/// Testes do <see cref="SpotifyApiClient"/>: mapeamento do JSON externo para os DTOs internos e o
/// cabeçalho de autenticação, usando um <see cref="HttpMessageHandler"/> mockado (sem rede real).
/// </summary>
public sealed class SpotifyApiClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _json;

        public HttpRequestMessage? LastRequest { get; private set; }

        public StubHandler(HttpStatusCode status, string json)
        {
            _status = status;
            _json = json;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            var response = new HttpResponseMessage(_status)
            {
                Content = new StringContent(_json, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class FixedTokenProvider : ISpotifyTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
            => Task.FromResult("fake-token");
    }

    private static SpotifyApiClient CreateClient(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.spotify.com/v1/") };
        return new SpotifyApiClient(http, new FixedTokenProvider());
    }

    [Fact]
    public async Task GetTrackAsync_MapsJson_ToInternalDto()
    {
        const string json = """
        {
          "id": "abc123",
          "name": "Bohemian Rhapsody",
          "popularity": 82,
          "duration_ms": 354000,
          "explicit": false,
          "artists": [ { "id": "q1", "name": "Queen" } ],
          "album": { "id": "alb1", "name": "A Night at the Opera" },
          "external_ids": { "isrc": "GBUM71029604" }
        }
        """;
        SpotifyApiClient client = CreateClient(new StubHandler(HttpStatusCode.OK, json));

        SpotifyTrack? track = await client.GetTrackAsync("abc123");

        Assert.NotNull(track);
        Assert.Equal("abc123", track!.Id);
        Assert.Equal("Bohemian Rhapsody", track.Name);
        Assert.Equal(82, track.Popularity);
        Assert.Equal(354000, track.DurationMs);
        Assert.False(track.Explicit);
        Assert.Single(track.Artists);
        Assert.Equal("Queen", track.Artists[0].Name);
        Assert.NotNull(track.Album);
        Assert.Equal("alb1", track.Album!.Id);
        Assert.Equal("GBUM71029604", track.Isrc);
    }

    /// <summary>
    /// A Spotify omite <c>external_ids</c> em parte do catálogo (faixas locais, lançamentos sem código
    /// registrado). O adapter tem de tratar a ausência como "sem ISRC", não como erro.
    /// </summary>
    [Theory]
    [InlineData("""{ "id": "x", "name": "n", "popularity": 0, "duration_ms": 0, "explicit": false, "artists": [], "album": null }""")]
    [InlineData("""{ "id": "x", "name": "n", "popularity": 0, "duration_ms": 0, "explicit": false, "artists": [], "album": null, "external_ids": {} }""")]
    [InlineData("""{ "id": "x", "name": "n", "popularity": 0, "duration_ms": 0, "explicit": false, "artists": [], "album": null, "external_ids": { "upc": "00602537" } }""")]
    public async Task GetTrackAsync_WithoutIsrc_MapsItAsAbsent(string json)
    {
        SpotifyApiClient client = CreateClient(new StubHandler(HttpStatusCode.OK, json));

        SpotifyTrack? track = await client.GetTrackAsync("x");

        Assert.NotNull(track);
        Assert.Null(track!.Isrc);
    }

    [Fact]
    public async Task GetTrackAsync_SetsBearerAuthorizationHeader_FromTokenProvider()
    {
        const string json = """
        { "id": "x", "name": "n", "popularity": 0, "duration_ms": 0, "explicit": false, "artists": [], "album": null }
        """;
        var handler = new StubHandler(HttpStatusCode.OK, json);
        SpotifyApiClient client = CreateClient(handler);

        await client.GetTrackAsync("x");

        AuthenticationHeaderValue? auth = handler.LastRequest?.Headers.Authorization;
        Assert.NotNull(auth);
        Assert.Equal("Bearer", auth!.Scheme);
        Assert.Equal("fake-token", auth.Parameter);
    }

    [Fact]
    public async Task GetTrackAsync_NotFound_ReturnsNull()
    {
        SpotifyApiClient client = CreateClient(new StubHandler(HttpStatusCode.NotFound, string.Empty));

        SpotifyTrack? track = await client.GetTrackAsync("missing");

        Assert.Null(track);
    }

    [Fact]
    public async Task GetArtistAsync_MapsJson_ToInternalDto()
    {
        const string json = """
        {
          "id": "q1",
          "name": "Queen",
          "popularity": 84,
          "followers": { "total": 43210987 },
          "genres": [ "classic rock", "glam rock" ]
        }
        """;
        SpotifyApiClient client = CreateClient(new StubHandler(HttpStatusCode.OK, json));

        SpotifyArtist? artist = await client.GetArtistAsync("q1");

        Assert.NotNull(artist);
        Assert.Equal("q1", artist!.Id);
        Assert.Equal("Queen", artist.Name);
        Assert.Equal(84, artist.Popularity);
        Assert.Equal(43210987, artist.Followers);
        Assert.Equal(new[] { "classic rock", "glam rock" }, artist.Genres);
    }

    /// <summary>
    /// Campos opcionais ausentes viram valores neutros previsíveis (0 seguidores, lista de gêneros vazia) —
    /// nunca <see langword="null"/>, para o consumidor não precisar se defender.
    /// </summary>
    [Fact]
    public async Task GetArtistAsync_WithoutFollowersOrGenres_MapsToNeutralValues()
    {
        const string json = """{ "id": "q1", "name": "Queen", "popularity": 0 }""";
        SpotifyApiClient client = CreateClient(new StubHandler(HttpStatusCode.OK, json));

        SpotifyArtist? artist = await client.GetArtistAsync("q1");

        Assert.NotNull(artist);
        Assert.Equal(0, artist!.Followers);
        Assert.Empty(artist.Genres);
    }

    [Fact]
    public async Task GetArtistAsync_NotFound_ReturnsNull()
    {
        SpotifyApiClient client = CreateClient(new StubHandler(HttpStatusCode.NotFound, string.Empty));

        Assert.Null(await client.GetArtistAsync("missing"));
    }

    [Fact]
    public async Task GetAlbumAsync_MapsJson_ToInternalDto()
    {
        const string json = """
        {
          "id": "alb1",
          "name": "A Night at the Opera",
          "release_date": "1975-11-21",
          "total_tracks": 12
        }
        """;
        SpotifyApiClient client = CreateClient(new StubHandler(HttpStatusCode.OK, json));

        SpotifyAlbum? album = await client.GetAlbumAsync("alb1");

        Assert.NotNull(album);
        Assert.Equal("alb1", album!.Id);
        Assert.Equal("A Night at the Opera", album.Name);
        Assert.Equal("1975-11-21", album.ReleaseDate);
        Assert.Equal(12, album.TotalTracks);
    }

    /// <summary>
    /// A Spotify devolve <c>release_date</c> em precisão variável (ano, ano-mês ou data completa) e às vezes
    /// nem devolve. O adapter repassa a string crua — interpretar a precisão é do domínio (<c>ReleaseDate</c>).
    /// </summary>
    [Theory]
    [InlineData("""{ "id": "alb1", "name": "Álbum", "release_date": "1975", "total_tracks": 12 }""", "1975")]
    [InlineData("""{ "id": "alb1", "name": "Álbum", "release_date": "1975-11", "total_tracks": 12 }""", "1975-11")]
    [InlineData("""{ "id": "alb1", "name": "Álbum", "total_tracks": 12 }""", null)]
    public async Task GetAlbumAsync_PassesTheRawReleaseDate_WhateverItsPrecision(
        string json, string? expected)
    {
        SpotifyApiClient client = CreateClient(new StubHandler(HttpStatusCode.OK, json));

        SpotifyAlbum? album = await client.GetAlbumAsync("alb1");

        Assert.NotNull(album);
        Assert.Equal(expected, album!.ReleaseDate);
    }

    [Fact]
    public async Task GetAlbumAsync_NotFound_ReturnsNull()
    {
        SpotifyApiClient client = CreateClient(new StubHandler(HttpStatusCode.NotFound, string.Empty));

        Assert.Null(await client.GetAlbumAsync("missing"));
    }

    // ---- Endpoints em lote (E1.8) ----

    /// <summary>
    /// Handler que devolve, por requisição, o próximo JSON da fila e registra cada URL chamada — para provar
    /// quantas requisições HTTP o adapter emitiu ao fatiar um lote grande.
    /// </summary>
    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;

        public QueueHandler(params string[] responses) => _responses = new Queue<string>(responses);

        public List<string> RequestedUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUris.Add(request.RequestUri!.PathAndQuery);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responses.Dequeue(), System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task GetArtistsAsync_MapsBatch_AndDropsNullPositionsForUnknownIds()
    {
        // A API devolve a lista "artists" com null na posição de um id inválido.
        const string json = """
        {
          "artists": [
            { "id": "q1", "name": "Queen", "popularity": 84, "followers": { "total": 100 }, "genres": ["rock"] },
            null,
            { "id": "b1", "name": "Bowie", "popularity": 80, "followers": { "total": 90 }, "genres": [] }
          ]
        }
        """;
        var handler = new QueueHandler(json);
        SpotifyApiClient client = CreateClient(handler);

        IReadOnlyList<SpotifyArtist> artists = await client.GetArtistsAsync(["q1", "invalido", "b1"]);

        Assert.Equal(2, artists.Count);
        Assert.Equal("q1", artists[0].Id);
        Assert.Equal("b1", artists[1].Id);
        Assert.Single(handler.RequestedUris); // 3 ids < 50: uma única requisição
        Assert.Contains("ids=q1,invalido,b1", handler.RequestedUris[0]);
    }

    [Fact]
    public async Task GetArtistsAsync_SplitsInto50IdChunks()
    {
        // Duas respostas vazias bastam: só nos importa quantas requisições o adapter emitiu.
        const string empty = """{ "artists": [] }""";
        var handler = new QueueHandler(empty, empty);
        SpotifyApiClient client = CreateClient(handler);

        string[] ids = Enumerable.Range(0, 51).Select(i => $"a{i}").ToArray();
        await client.GetArtistsAsync(ids);

        // 51 ids > teto de 50 por chamada ⇒ dois lotes.
        Assert.Equal(2, handler.RequestedUris.Count);
    }

    [Fact]
    public async Task GetAlbumsAsync_MapsBatch_AndSplitsInto20IdChunks()
    {
        const string first = """
        {
          "albums": [
            { "id": "al1", "name": "One", "release_date": "1975", "total_tracks": 12 }
          ]
        }
        """;
        const string second = """{ "albums": [] }""";
        var handler = new QueueHandler(first, second);
        SpotifyApiClient client = CreateClient(handler);

        string[] ids = Enumerable.Range(0, 21).Select(i => $"al{i}").ToArray();
        IReadOnlyList<SpotifyAlbum> albums = await client.GetAlbumsAsync(ids);

        // 21 ids > teto de 20 por chamada ⇒ dois lotes.
        Assert.Equal(2, handler.RequestedUris.Count);
        SpotifyAlbum album = Assert.Single(albums);
        Assert.Equal("al1", album.Id);
        Assert.Equal(12, album.TotalTracks);
    }

    [Fact]
    public async Task GetArtistsAsync_EmptyInput_MakesNoHttpCall()
    {
        var handler = new QueueHandler();
        SpotifyApiClient client = CreateClient(handler);

        IReadOnlyList<SpotifyArtist> artists = await client.GetArtistsAsync([]);

        Assert.Empty(artists);
        Assert.Empty(handler.RequestedUris);
    }

    [Fact]
    public async Task GetPlaylistAsync_MapsMetadata_AndRequestsOnlyTheNeededFields()
    {
        const string json = """
        {
          "id": "pl1",
          "name": "Minha semente",
          "owner": { "display_name": "kaua" },
          "tracks": { "total": 137 }
        }
        """;
        var handler = new StubHandler(HttpStatusCode.OK, json);
        SpotifyApiClient client = CreateClient(handler);

        SpotifyPlaylist? playlist = await client.GetPlaylistAsync("pl1");

        Assert.NotNull(playlist);
        Assert.Equal("pl1", playlist!.Id);
        Assert.Equal("Minha semente", playlist.Name);
        Assert.Equal("kaua", playlist.OwnerDisplayName);
        Assert.Equal(137, playlist.TotalTracks);

        // O filtro "fields" evita trazer a primeira página de faixas junto (payload muito maior).
        Assert.Contains("fields=", handler.LastRequest!.RequestUri!.Query);
    }

    [Fact]
    public async Task GetPlaylistAsync_WithoutOwner_MapsTheDisplayNameAsAbsent()
    {
        const string json = """{ "id": "pl1", "name": "Sem dono", "tracks": { "total": 0 } }""";
        SpotifyApiClient client = CreateClient(new StubHandler(HttpStatusCode.OK, json));

        SpotifyPlaylist? playlist = await client.GetPlaylistAsync("pl1");

        Assert.NotNull(playlist);
        Assert.Null(playlist!.OwnerDisplayName);
        Assert.Equal(0, playlist.TotalTracks);
    }

    [Fact]
    public async Task GetPlaylistTracksAsync_MapsItems_AndComputesHasNext()
    {
        const string json = """
        {
          "items": [
            { "track": { "id": "t1", "name": "One", "popularity": 10, "duration_ms": 1000, "explicit": false, "artists": [], "album": null, "external_ids": { "isrc": "USUM71703861" } } },
            { "track": { "id": "t2", "name": "Two", "popularity": 20, "duration_ms": 2000, "explicit": true,  "artists": [], "album": null } }
          ],
          "offset": 0,
          "limit": 2,
          "total": 5
        }
        """;
        SpotifyApiClient client = CreateClient(new StubHandler(HttpStatusCode.OK, json));

        SpotifyPlaylistTracksPage page = await client.GetPlaylistTracksAsync("pl1", 0, 2);

        Assert.Equal(2, page.Items.Count);
        Assert.Equal("t2", page.Items[1].Id);
        Assert.Equal(5, page.Total);
        Assert.True(page.HasNext); // 0 + 2 < 5

        // O ISRC vem do full track object dos itens da playlist — é o caminho real da ingestão.
        Assert.Equal("USUM71703861", page.Items[0].Isrc);
        Assert.Null(page.Items[1].Isrc);
    }
}
