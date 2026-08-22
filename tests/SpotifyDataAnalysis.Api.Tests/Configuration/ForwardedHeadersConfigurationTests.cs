using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using SpotifyDataAnalysis.Api.Configuration;
using IPNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

namespace SpotifyDataAnalysis.Api.Tests.Configuration;

/// <summary>
/// Unit coverage for the pure settings→<see cref="ForwardedHeadersOptions"/> translation. These assert the
/// security posture (which origins are trusted, which headers are honored) without booting the host, so the
/// invariant — "never trust forged headers from arbitrary origins in production" — is guarded at the source.
/// </summary>
public sealed class ForwardedHeadersConfigurationTests
{
    [Fact]
    public void ApplyTo_AlwaysForwards_ForAndProto()
    {
        var options = new ForwardedHeadersOptions();

        ForwardedHeadersConfiguration.ApplyTo(options, new ForwardedHeadersSettings(), isDevelopment: false);

        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedFor));
        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedProto));
    }

    [Fact]
    public void ApplyTo_DoesNotForwardHost_ByDefault()
    {
        var options = new ForwardedHeadersOptions();

        ForwardedHeadersConfiguration.ApplyTo(options, new ForwardedHeadersSettings(), isDevelopment: false);

        Assert.False(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedHost));
    }

    [Fact]
    public void ApplyTo_ForwardsHost_WhenOptedIn()
    {
        var options = new ForwardedHeadersOptions();
        var settings = new ForwardedHeadersSettings { ForwardHost = true };

        ForwardedHeadersConfiguration.ApplyTo(options, settings, isDevelopment: false);

        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedHost));
    }

    [Fact]
    public void ApplyTo_AddsConfiguredProxy_WithoutDroppingLoopbackDefaults()
    {
        var options = new ForwardedHeadersOptions();
        int loopbackDefaults = options.KnownProxies.Count;
        var settings = new ForwardedHeadersSettings { KnownProxies = ["203.0.113.7"] };

        ForwardedHeadersConfiguration.ApplyTo(options, settings, isDevelopment: false);

        Assert.Contains(IPAddress.Parse("203.0.113.7"), options.KnownProxies);
        Assert.Equal(loopbackDefaults + 1, options.KnownProxies.Count);
    }

    [Fact]
    public void ApplyTo_AddsConfiguredCidrNetwork()
    {
        var options = new ForwardedHeadersOptions();
        var settings = new ForwardedHeadersSettings { KnownNetworks = ["172.16.0.0/12"] };

        ForwardedHeadersConfiguration.ApplyTo(options, settings, isDevelopment: false);

        Assert.Contains(
            options.KnownNetworks,
            n => n.Prefix.Equals(IPAddress.Parse("172.16.0.0")) && n.PrefixLength == 12);
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("172.16.0.0")]      // no prefix length
    [InlineData("172.16.0.0/33")]   // prefix out of IPv4 range
    [InlineData("172.16.0.0/-1")]   // negative prefix
    public void ApplyTo_IgnoresMalformedNetworkEntries(string malformed)
    {
        var options = new ForwardedHeadersOptions();
        int loopbackDefaults = options.KnownNetworks.Count;
        var settings = new ForwardedHeadersSettings { KnownNetworks = [malformed] };

        ForwardedHeadersConfiguration.ApplyTo(options, settings, isDevelopment: false);

        // Nothing added beyond the framework's loopback default — a malformed CIDR is silently skipped, never
        // widening trust.
        Assert.Equal(loopbackDefaults, options.KnownNetworks.Count);
    }

    [Fact]
    public void ApplyTo_InProduction_IgnoresTrustAllProxies_KeepingLoopbackDefaults()
    {
        var options = new ForwardedHeadersOptions();
        int loopbackProxies = options.KnownProxies.Count;
        int loopbackNetworks = options.KnownNetworks.Count;
        var settings = new ForwardedHeadersSettings { TrustAllProxies = true };

        ForwardedHeadersConfiguration.ApplyTo(options, settings, isDevelopment: false);

        // Production must never accept forwarded headers from arbitrary origins: the loopback defaults survive.
        Assert.Equal(loopbackProxies, options.KnownProxies.Count);
        Assert.Equal(loopbackNetworks, options.KnownNetworks.Count);
    }

    [Fact]
    public void ApplyTo_InDevelopment_TrustAllProxies_ClearsKnownLists()
    {
        var options = new ForwardedHeadersOptions();
        var settings = new ForwardedHeadersSettings { TrustAllProxies = true };

        ForwardedHeadersConfiguration.ApplyTo(options, settings, isDevelopment: true);

        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownNetworks);
    }

    [Fact]
    public void IsBehindProxy_TracksEnabledFlag()
    {
        Assert.False(ForwardedHeadersConfiguration.IsBehindProxy(new ForwardedHeadersSettings()));
        Assert.True(ForwardedHeadersConfiguration.IsBehindProxy(new ForwardedHeadersSettings { Enabled = true }));
    }
}
