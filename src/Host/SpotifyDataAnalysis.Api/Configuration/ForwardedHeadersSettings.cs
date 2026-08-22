namespace SpotifyDataAnalysis.Api.Configuration;

/// <summary>
/// Per-environment trust configuration for the <c>X-Forwarded-*</c> headers emitted by an upstream
/// reverse proxy (Caddy terminates TLS and forwards to the API over plain HTTP on the internal network).
///
/// <para>Bound from the <c>ForwardedHeaders</c> configuration section. It exists so proxy trust is
/// <b>declarative and environment-scoped</b>: the Docker bridge IP the proxy arrives from is only known at
/// deploy time, so it is supplied via configuration/environment variables — never hard-coded here.</para>
///
/// <para><b>Security invariant.</b> ASP.NET Core only trusts loopback proxies out of the box; behind Docker
/// the proxy is a non-loopback bridge address, and the tempting "fix" is to clear the known lists so any
/// origin is trusted. That would let <b>any client forge <c>X-Forwarded-For</c>/<c>X-Forwarded-Proto</c></b>,
/// poisoning access logs and every IP-based decision. This type never clears the known lists on the caller's
/// behalf: production must name its proxy via <see cref="KnownProxies"/>/<see cref="KnownNetworks"/>.
/// <see cref="TrustAllProxies"/> is a deliberate, documented escape hatch for local development only.</para>
/// </summary>
public sealed class ForwardedHeadersSettings
{
    /// <summary>Configuration section this settings object binds to.</summary>
    public const string SectionName = "ForwardedHeaders";

    /// <summary>
    /// Whether the Host is deployed behind a trusted reverse proxy. When <c>false</c> (default), the
    /// forwarded-headers middleware is not applied and the API reads scheme/host/IP straight from Kestrel —
    /// the correct behavior for local runs and direct exposure. Turned on only where a proxy actually fronts
    /// the API (e.g. the VPS behind Caddy).
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Individual proxy IP addresses (IPv4 or IPv6) whose <c>X-Forwarded-*</c> headers are trusted. Combine
    /// with <see cref="KnownNetworks"/>. Empty by default so nothing beyond loopback is trusted implicitly.
    /// </summary>
    public IReadOnlyList<string> KnownProxies { get; init; } = [];

    /// <summary>
    /// Trusted proxy networks in CIDR notation (e.g. <c>172.16.0.0/12</c> for a Docker bridge). Preferred
    /// over <see cref="KnownProxies"/> when the proxy's exact address is assigned dynamically by the
    /// container runtime but its subnet is stable.
    /// </summary>
    public IReadOnlyList<string> KnownNetworks { get; init; } = [];

    /// <summary>
    /// Maximum number of proxy hops to walk back through the forwarded chain. Defaults to <c>1</c> — a single
    /// TLS-terminating proxy (Caddy) directly in front of the API. Raise it only if additional trusted hops
    /// sit in between.
    /// </summary>
    public int ForwardLimit { get; init; } = 1;

    /// <summary>
    /// Also honor <c>X-Forwarded-Host</c>. Off by default: the host is pinned via <c>AllowedHosts</c> and an
    /// attacker-controlled host header is a poisoning vector, so it is opt-in per environment.
    /// </summary>
    public bool ForwardHost { get; init; }

    /// <summary>
    /// <b>Development-only escape hatch.</b> When <c>true</c>, clears the known-proxy/known-network lists so
    /// forwarded headers are accepted from any origin. Convenient for reproducing proxy behavior on the local
    /// machine; <b>never</b> enable in production — it is exactly the "trust everyone" posture the card
    /// forbids. Ignored (and treated as a misconfiguration) unless the environment is Development.
    /// </summary>
    public bool TrustAllProxies { get; init; }
}
