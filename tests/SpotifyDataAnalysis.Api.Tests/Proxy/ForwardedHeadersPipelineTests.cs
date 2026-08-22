using System.Net;
using System.Net.Http.Json;

namespace SpotifyDataAnalysis.Api.Tests.Proxy;

/// <summary>
/// End-to-end coverage of the forwarded-headers behavior on the real Host pipeline (booted on a
/// <c>TestServer</c>). Two scenarios matter for the card:
///
/// <list type="bullet">
///   <item><b>Behind a trusted proxy</b> — a request carrying <c>X-Forwarded-Proto: https</c> and an
///   <c>X-Forwarded-For</c> client IP, arriving from the known proxy address, is seen by the app as
///   <c>https</c> with the <i>real</i> client IP (not the proxy's).</item>
///   <item><b>Not behind a proxy</b> — the same headers are ignored and the raw scheme/IP are preserved,
///   so a direct client cannot forge scheme or IP.</item>
/// </list>
/// </summary>
public sealed class ForwardedHeadersPipelineTests
{
    private const string RealClientIp = "203.0.113.42";

    [Fact]
    public async Task BehindProxy_RequestWithForwardedHeaders_IsSeenAsHttpsWithRealClientIp()
    {
        using var factory = new ProxyScenarioFactory(behindProxy: true);
        using HttpClient client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, ProxyScenarioFactory.ProbePath);
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", RealClientIp);

        using HttpResponseMessage response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        SchemeProbe? probe = await response.Content.ReadFromJsonAsync<SchemeProbe>();

        Assert.NotNull(probe);
        Assert.Equal("https", probe!.Scheme);
        Assert.Equal(RealClientIp, probe.ClientIp);
    }

    [Fact]
    public async Task BehindProxy_RequestWithoutForwardedHeaders_KeepsProxyConnectionScheme()
    {
        using var factory = new ProxyScenarioFactory(behindProxy: true);
        using HttpClient client = factory.CreateClient();

        // No X-Forwarded-* headers: nothing to rewrite, so the app sees the direct (proxy) connection.
        using HttpResponseMessage response = await client.GetAsync(ProxyScenarioFactory.ProbePath);
        response.EnsureSuccessStatusCode();

        SchemeProbe? probe = await response.Content.ReadFromJsonAsync<SchemeProbe>();

        Assert.NotNull(probe);
        // TestServer speaks http on the loopback; without forwarded headers the scheme is not upgraded and
        // the client IP is the immediate hop (the proxy), never a forged value.
        Assert.Equal("http", probe!.Scheme);
        Assert.Equal(ProxyScenarioFactory.ProxyIp, probe.ClientIp);
    }

    [Fact]
    public async Task NotBehindProxy_ForwardedHeadersAreIgnored_SchemeNotUpgraded()
    {
        using var factory = new ProxyScenarioFactory(behindProxy: false);
        using HttpClient client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, ProxyScenarioFactory.ProbePath);
        // A direct client forging the headers must NOT be able to upgrade the scheme or spoof its IP.
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", RealClientIp);

        using HttpResponseMessage response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        SchemeProbe? probe = await response.Content.ReadFromJsonAsync<SchemeProbe>();

        Assert.NotNull(probe);
        Assert.Equal("http", probe!.Scheme);
        Assert.NotEqual(RealClientIp, probe.ClientIp);
    }

    [Fact]
    public async Task Application_Boots_WithForwardedHeadersWiring()
    {
        // Guards that adding the forwarded-headers wiring did not break host startup: a booting host serves
        // the pipeline (asserted via the diagnostic endpoint, which needs no database).
        using var factory = new ProxyScenarioFactory(behindProxy: true);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(ProxyScenarioFactory.ProbePath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
