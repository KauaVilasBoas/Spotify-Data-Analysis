using System.Net;
using System.Net.Http.Headers;
using SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Spotify;

namespace SpotifyDataAnalysis.Modules.Catalog.Tests.Spotify;

/// <summary>
/// Testes do <see cref="SpotifyResilienceHandler"/>: retry em falhas transitórias com backoff, respeito ao
/// <c>Retry-After</c> no 429, desistência após o máximo de tentativas e ausência de retry em erros
/// não-transitórios. Sem espera real — o atraso é um delegate no-op que apenas registra os tempos.
/// </summary>
public sealed class SpotifyResilienceHandlerTests
{
    private sealed class SequencedHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public int CallCount { get; private set; }

        public SequencedHandler(params HttpResponseMessage[] responses) => _responses = new Queue<HttpResponseMessage>(responses);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private static HttpResponseMessage Status(HttpStatusCode code)
        => new(code) { Content = new StringContent("{}") };

    private static (HttpClient Client, List<TimeSpan> Delays) BuildClient(int maxRetries, HttpMessageHandler inner)
    {
        var delays = new List<TimeSpan>();
        var handler = new SpotifyResilienceHandler(
            maxRetries,
            TimeSpan.FromMilliseconds(10),
            (delay, _) => { delays.Add(delay); return Task.CompletedTask; })
        {
            InnerHandler = inner
        };
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.spotify.com/v1/") };
        return (client, delays);
    }

    [Fact]
    public async Task Retries_OnTransientStatus_ThenSucceeds()
    {
        var inner = new SequencedHandler(Status(HttpStatusCode.ServiceUnavailable), Status(HttpStatusCode.OK));
        (HttpClient client, List<TimeSpan> delays) = BuildClient(maxRetries: 3, inner);

        HttpResponseMessage response = await client.GetAsync("tracks/x");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.CallCount);
        Assert.Single(delays);
    }

    [Fact]
    public async Task Honors_RetryAfterHeader_On429()
    {
        HttpResponseMessage tooMany = Status(HttpStatusCode.TooManyRequests);
        tooMany.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(5));

        var inner = new SequencedHandler(tooMany, Status(HttpStatusCode.OK));
        (HttpClient client, List<TimeSpan> delays) = BuildClient(maxRetries: 3, inner);

        HttpResponseMessage response = await client.GetAsync("tracks/x");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(delays);
        Assert.Equal(TimeSpan.FromSeconds(5), delays[0]); // esperou o Retry-After, não o backoff
    }

    [Fact]
    public async Task GivesUp_AfterMaxRetries_ReturningLastTransientResponse()
    {
        var inner = new SequencedHandler(
            Status(HttpStatusCode.ServiceUnavailable),
            Status(HttpStatusCode.ServiceUnavailable),
            Status(HttpStatusCode.ServiceUnavailable));
        (HttpClient client, List<TimeSpan> delays) = BuildClient(maxRetries: 2, inner);

        HttpResponseMessage response = await client.GetAsync("tracks/x");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, inner.CallCount); // 1 tentativa + 2 retries
        Assert.Equal(2, delays.Count);
    }

    [Fact]
    public async Task DoesNotRetry_OnNonTransientStatus()
    {
        var inner = new SequencedHandler(Status(HttpStatusCode.BadRequest));
        (HttpClient client, List<TimeSpan> delays) = BuildClient(maxRetries: 3, inner);

        HttpResponseMessage response = await client.GetAsync("tracks/x");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, inner.CallCount);
        Assert.Empty(delays);
    }
}
