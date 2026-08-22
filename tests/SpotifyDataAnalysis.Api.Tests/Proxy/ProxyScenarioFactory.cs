using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace SpotifyDataAnalysis.Api.Tests.Proxy;

/// <summary>
/// Boots the real Host (<c>Program</c>) on an in-memory <c>TestServer</c> to exercise the forwarded-headers
/// configuration end to end. It layers two test-only concerns on top of the production composition root:
///
/// <list type="number">
///   <item><b>Configuration override</b> — sets <c>ForwardedHeaders:Enabled</c> and trusts the simulated
///   proxy IP, mirroring the VPS-behind-Caddy topology without touching production config. The environment
///   is forced to Production so the dev-only escape hatch can never mask the trust check.</item>
///   <item><b>A diagnostic branch</b> (<c>/__test/scheme</c>) injected via <see cref="IStartupFilter"/>. It
///   stamps the simulated proxy's <c>RemoteIpAddress</c> on the connection (<c>TestServer</c> leaves it null,
///   and the forwarded-headers middleware only trusts a <i>known</i> proxy address), runs the SAME configured
///   <see cref="Microsoft.AspNetCore.Builder.ForwardedHeadersOptions"/> the production pipeline registers, and
///   echoes the resulting scheme and client IP. Resolving the options from DI is what makes this a faithful
///   test of <c>AddSpotifyForwardedHeaders</c> rather than a hand-rolled duplicate.</item>
/// </list>
///
/// The branch exists only in the test host, so the production API never exposes it.
/// </summary>
public sealed class ProxyScenarioFactory : WebApplicationFactory<Program>
{
    /// <summary>IP the test middleware presents as the immediate (proxy) connection.</summary>
    public const string ProxyIp = "10.10.0.5";

    /// <summary>Diagnostic path served only in the test host.</summary>
    public const string ProbePath = "/__test/scheme";

    private readonly bool _behindProxy;

    public ProxyScenarioFactory(bool behindProxy) => _behindProxy = behindProxy;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Production);

        // The modules require a SpotifyDb connection string at registration to configure their DbContexts.
        // A syntactically valid placeholder is enough: Npgsql doesn't open a connection at registration, and
        // the diagnostic branch under test never touches the database.
        builder.UseSetting(
            "ConnectionStrings:SpotifyDb",
            "Host=localhost;Port=5432;Database=spotify_test;Username=test;Password=test");

        builder.UseSetting("ForwardedHeaders:Enabled", _behindProxy ? "true" : "false");
        builder.UseSetting("ForwardedHeaders:ForwardLimit", "1");
        builder.UseSetting("ForwardedHeaders:TrustAllProxies", "false");
        // Trust exactly the simulated proxy address — never a wildcard.
        builder.UseSetting("ForwardedHeaders:KnownProxies:0", ProxyIp);

        builder.ConfigureServices(services =>
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IStartupFilter>(new DiagnosticStartupFilter(_behindProxy))));
    }

    /// <summary>
    /// Prepends a diagnostic branch that reproduces the production forwarded-headers behavior against the SAME
    /// configured options, so the assertions observe the real trust decision.
    /// </summary>
    private sealed class DiagnosticStartupFilter : IStartupFilter
    {
        private readonly IPAddress _proxyIp;
        private readonly bool _behindProxy;

        public DiagnosticStartupFilter(bool behindProxy)
        {
            _proxyIp = IPAddress.Parse(ProxyIp);
            _behindProxy = behindProxy;
        }

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.MapWhen(
                context => context.Request.Path == ProbePath,
                branch =>
                {
                    // Simulate the immediate hop being the trusted proxy, exactly as it arrives on the VPS.
                    branch.Use(async (context, request) =>
                    {
                        context.Connection.RemoteIpAddress = _proxyIp;
                        await request(context);
                    });

                    // Apply the very ForwardedHeadersOptions the production composition root registered
                    // (resolved from DI by the parameterless overload). Skipped when not behind a proxy, so
                    // the "no proxy mode" scenario preserves the raw scheme/IP.
                    if (_behindProxy)
                        branch.UseForwardedHeaders();

                    branch.Run(async context =>
                    {
                        string clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                        await context.Response.WriteAsJsonAsync(new SchemeProbe(context.Request.Scheme, clientIp));
                    });
                });

            next(app);
        };
    }
}

/// <summary>Shape returned by the <c>/__test/scheme</c> diagnostic endpoint.</summary>
public sealed record SchemeProbe(string Scheme, string ClientIp);
