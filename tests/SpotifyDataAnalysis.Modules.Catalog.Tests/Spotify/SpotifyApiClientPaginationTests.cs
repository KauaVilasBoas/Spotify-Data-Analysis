using System.Net;
using System.Text;
using SpotifyDataAnalysis.Modules.Catalog.Application.Spotify;
using SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Spotify;

namespace SpotifyDataAnalysis.Modules.Catalog.Tests.Spotify;

/// <summary>
/// Testa a paginação por streaming do <see cref="SpotifyApiClient.StreamPlaylistTracksAsync"/>: percorre
/// automaticamente todas as páginas (offset/limit/total) até <c>HasNext</c> ser falso.
/// </summary>
public sealed class SpotifyApiClientPaginationTests
{
    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<string> _pages;

        public int CallCount { get; private set; }

        public QueueHandler(params string[] pages) => _pages = new Queue<string>(pages);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_pages.Dequeue(), Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class FixedTokenProvider : ISpotifyTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
            => Task.FromResult("fake-token");
    }

    [Fact]
    public async Task StreamPlaylistTracksAsync_WalksAllPages()
    {
        const string page1 = """
        {
          "items": [
            { "track": { "id": "t1", "name": "One",   "popularity": 0, "duration_ms": 0, "explicit": false, "artists": [], "album": null } },
            { "track": { "id": "t2", "name": "Two",   "popularity": 0, "duration_ms": 0, "explicit": false, "artists": [], "album": null } }
          ],
          "offset": 0, "limit": 2, "total": 3
        }
        """;
        const string page2 = """
        {
          "items": [
            { "track": { "id": "t3", "name": "Three", "popularity": 0, "duration_ms": 0, "explicit": false, "artists": [], "album": null } }
          ],
          "offset": 2, "limit": 2, "total": 3
        }
        """;

        var handler = new QueueHandler(page1, page2);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.spotify.com/v1/") };
        var client = new SpotifyApiClient(http, new FixedTokenProvider());

        var ids = new List<string>();
        await foreach (SpotifyTrack track in client.StreamPlaylistTracksAsync("pl1", pageSize: 2))
            ids.Add(track.Id);

        Assert.Equal(new[] { "t1", "t2", "t3" }, ids);
        Assert.Equal(2, handler.CallCount); // exatamente 2 páginas
    }
}
