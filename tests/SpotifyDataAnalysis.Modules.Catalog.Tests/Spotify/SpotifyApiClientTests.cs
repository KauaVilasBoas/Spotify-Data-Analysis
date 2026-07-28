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

    private static SpotifyApiClient CreateClient(StubHandler handler)
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
          "album": { "id": "alb1", "name": "A Night at the Opera" }
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
    public async Task GetPlaylistTracksAsync_MapsItems_AndComputesHasNext()
    {
        const string json = """
        {
          "items": [
            { "track": { "id": "t1", "name": "One", "popularity": 10, "duration_ms": 1000, "explicit": false, "artists": [], "album": null } },
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
    }
}
