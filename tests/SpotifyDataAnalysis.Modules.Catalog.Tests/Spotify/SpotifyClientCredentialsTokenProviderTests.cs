using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Spotify;
using SpotifyDataAnalysis.SharedKernel.Time;

namespace SpotifyDataAnalysis.Modules.Catalog.Tests.Spotify;

/// <summary>
/// Testes do <see cref="SpotifyClientCredentialsTokenProvider"/>: obtém o token (Basic + grant_type),
/// cacheia (não refaz HTTP), renova após expirar (via <see cref="IClock"/> fake) e falha claramente sem
/// credenciais — tudo com um <see cref="HttpMessageHandler"/> mockado (sem rede).
/// </summary>
public sealed class SpotifyClientCredentialsTokenProviderTests
{
    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly Func<int, HttpResponseMessage> _responder;

        public int CallCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        public CountingHandler(Func<int, HttpResponseMessage> responder) => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            if (request.Content is not null)
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return _responder(CallCount);
        }
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;

        // disposeHandler:false — o mesmo handler é reusado entre CreateClient()s (mantém o CallCount).
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class MutableClock : IClock
    {
        public DateTime UtcNow { get; set; } = new(2026, 7, 28, 12, 0, 0, DateTimeKind.Utc);
    }

    private static HttpResponseMessage TokenResponse(string accessToken, int expiresIn)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"access_token\":\"" + accessToken + "\",\"token_type\":\"Bearer\",\"expires_in\":" + expiresIn + "}",
                Encoding.UTF8, "application/json")
        };

    private static SpotifyClientCredentialsTokenProvider CreateProvider(
        HttpMessageHandler handler, IClock clock, string clientId = "the-id", string clientSecret = "the-secret")
    {
        var options = Options.Create(new SpotifyApiOptions
        {
            AuthUrl = "https://accounts.spotify.com/api/token",
            ClientId = clientId,
            ClientSecret = clientSecret
        });
        return new SpotifyClientCredentialsTokenProvider(new SingleClientFactory(handler), options, clock);
    }

    [Fact]
    public async Task GetAccessTokenAsync_FetchesToken_WithBasicAuthAndGrantType()
    {
        var handler = new CountingHandler(_ => TokenResponse("tok-1", 3600));
        SpotifyClientCredentialsTokenProvider provider = CreateProvider(handler, new MutableClock());

        string token = await provider.GetAccessTokenAsync();

        Assert.Equal("tok-1", token);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("https://accounts.spotify.com/api/token", handler.LastRequest.RequestUri!.ToString());
        Assert.Equal("Basic", handler.LastRequest.Headers.Authorization!.Scheme);

        string expectedBasic = Convert.ToBase64String(Encoding.UTF8.GetBytes("the-id:the-secret"));
        Assert.Equal(expectedBasic, handler.LastRequest.Headers.Authorization.Parameter);
        Assert.Contains("grant_type=client_credentials", handler.LastBody);
    }

    [Fact]
    public async Task GetAccessTokenAsync_CachesToken_WhileValid()
    {
        var handler = new CountingHandler(_ => TokenResponse("tok-1", 3600));
        SpotifyClientCredentialsTokenProvider provider = CreateProvider(handler, new MutableClock());

        string first = await provider.GetAccessTokenAsync();
        string second = await provider.GetAccessTokenAsync();

        Assert.Equal("tok-1", first);
        Assert.Equal("tok-1", second);
        Assert.Equal(1, handler.CallCount); // segundo GetAccessToken não refaz HTTP
    }

    [Fact]
    public async Task GetAccessTokenAsync_RefreshesToken_AfterExpiry()
    {
        var handler = new CountingHandler(call => TokenResponse(call == 1 ? "tok-1" : "tok-2", 3600));
        var clock = new MutableClock();
        SpotifyClientCredentialsTokenProvider provider = CreateProvider(handler, clock);

        string first = await provider.GetAccessTokenAsync();
        clock.UtcNow = clock.UtcNow.AddSeconds(3600); // passa da validade (3600s - 60s de margem)
        string second = await provider.GetAccessTokenAsync();

        Assert.Equal("tok-1", first);
        Assert.Equal("tok-2", second);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task GetAccessTokenAsync_Throws_WhenCredentialsMissing()
    {
        var handler = new CountingHandler(_ => TokenResponse("tok-1", 3600));
        SpotifyClientCredentialsTokenProvider provider =
            CreateProvider(handler, new MutableClock(), clientId: "", clientSecret: "");

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetAccessTokenAsync());
        Assert.Equal(0, handler.CallCount); // nem tenta bater no endpoint
    }
}
