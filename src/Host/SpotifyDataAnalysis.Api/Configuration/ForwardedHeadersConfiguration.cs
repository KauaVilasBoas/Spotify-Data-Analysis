using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
// Disambiguate: both System.Net and Microsoft.AspNetCore.HttpOverrides expose IPNetwork in .NET 8, and
// ForwardedHeadersOptions.KnownNetworks is the ASP.NET Core type — alias it so the intent is explicit.
using IPNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

namespace SpotifyDataAnalysis.Api.Configuration;

/// <summary>
/// Composition-root wiring for the forwarded-headers middleware. Translates the environment-scoped
/// <see cref="ForwardedHeadersSettings"/> into a hardened <see cref="ForwardedHeadersOptions"/> and exposes
/// whether the Host is running in "behind a proxy" mode, so <c>Program.cs</c> can skip
/// <c>UseHttpsRedirection()</c> (redirect is the proxy's job, and calling it with no HTTPS endpoint is what
/// produces the "failed to determine the https port" warning).
///
/// <para>Lives entirely in the Host: this is edge/transport concern, it never leaks into any module.</para>
/// </summary>
public static class ForwardedHeadersConfiguration
{
    /// <summary>
    /// Binds <see cref="ForwardedHeadersSettings"/> from configuration and, when the Host is behind a trusted
    /// proxy, registers a hardened <see cref="ForwardedHeadersOptions"/>. Returns the bound settings so the
    /// caller can decide on middleware placement (see <see cref="IsBehindProxy"/>).
    /// </summary>
    public static ForwardedHeadersSettings AddSpotifyForwardedHeaders(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ForwardedHeadersSettings settings =
            configuration.GetSection(ForwardedHeadersSettings.SectionName).Get<ForwardedHeadersSettings>()
            ?? new ForwardedHeadersSettings();

        if (!IsBehindProxy(settings))
            return settings;

        services.Configure<ForwardedHeadersOptions>(options =>
            ApplyTo(options, settings, environment.IsDevelopment()));

        return settings;
    }

    /// <summary>
    /// Whether the forwarded-headers middleware should run and <c>UseHttpsRedirection()</c> should be skipped.
    /// Proxy mode is opt-in via <see cref="ForwardedHeadersSettings.Enabled"/>.
    /// </summary>
    public static bool IsBehindProxy(ForwardedHeadersSettings settings) => settings.Enabled;

    /// <summary>
    /// Pure translation of the settings into <see cref="ForwardedHeadersOptions"/>, extracted so it can be
    /// unit-tested without spinning up the host. Kept in one place so the security posture (which origins are
    /// trusted) is auditable at a glance.
    ///
    /// <list type="bullet">
    ///   <item>Always forwards <c>X-Forwarded-For</c> (client IP) and <c>X-Forwarded-Proto</c> (real scheme);
    ///   <c>X-Forwarded-Host</c> only when explicitly opted in.</item>
    ///   <item>Adds the configured proxies/networks to the framework defaults ({loopback}), never replacing
    ///   them — so loopback keeps working and the configured proxy is additionally trusted.</item>
    ///   <item>Clears the known lists (accept from anywhere) <b>only</b> when
    ///   <see cref="ForwardedHeadersSettings.TrustAllProxies"/> is set <b>and</b> the environment is
    ///   Development. In production the flag is ignored: the API never trusts an unbounded set of origins.</item>
    /// </list>
    /// </summary>
    public static void ApplyTo(
        ForwardedHeadersOptions options,
        ForwardedHeadersSettings settings,
        bool isDevelopment)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        if (settings.ForwardHost)
            options.ForwardedHeaders |= ForwardedHeaders.XForwardedHost;

        options.ForwardLimit = settings.ForwardLimit;

        // Dev-only "trust everyone": clearing the known lists tells the middleware to accept X-Forwarded-*
        // from any caller. Gated on Development so a stray production flag can never open this up.
        if (settings.TrustAllProxies && isDevelopment)
        {
            options.KnownProxies.Clear();
            options.KnownNetworks.Clear();
            return;
        }

        foreach (string proxy in settings.KnownProxies)
        {
            if (IPAddress.TryParse(proxy, out IPAddress? address))
                options.KnownProxies.Add(address);
        }

        foreach (string network in settings.KnownNetworks)
        {
            if (TryParseNetwork(network, out IPNetwork parsed))
                options.KnownNetworks.Add(parsed);
        }
    }

    /// <summary>Parses a CIDR string (e.g. <c>172.16.0.0/12</c>) into an <see cref="IPNetwork"/>.</summary>
    private static bool TryParseNetwork(string cidr, out IPNetwork network)
    {
        network = default!;

        string[] parts = cidr.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return false;

        if (!IPAddress.TryParse(parts[0], out IPAddress? prefix))
            return false;

        if (!int.TryParse(parts[1], out int prefixLength) || prefixLength < 0)
            return false;

        int maxPrefix = prefix.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32;
        if (prefixLength > maxPrefix)
            return false;

        network = new IPNetwork(prefix, prefixLength);
        return true;
    }
}
