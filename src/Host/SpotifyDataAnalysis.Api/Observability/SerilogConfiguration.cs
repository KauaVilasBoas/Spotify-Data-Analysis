using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using SpotifyDataAnalysis.SharedKernel.Observability;

namespace SpotifyDataAnalysis.Api.Observability;

/// <summary>
/// Wires Serilog for the Host: structured JSON logs enriched with the request <c>CorrelationId</c> and a
/// Console sink. Reads any <c>Serilog</c> section from configuration first, then enriches with the
/// application name and correlation id.
///
/// <para>Secrets hygiene: the application never logs passwords, tokens or cookies. Serilog only emits the
/// structured properties the application explicitly logs (request name, duration, correlation id,
/// exception type/message) plus the framework request log — none of which carry credentials.</para>
/// </summary>
internal static class SerilogConfiguration
{
    /// <summary>
    /// Builds the Serilog logger configuration for the given host context.
    /// </summary>
    public static void Configure(HostBuilderContext context, LoggerConfiguration configuration)
    {
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty(
                ObservabilityConstants.ApplicationProperty, ObservabilityConstants.ApplicationName)
            .WriteTo.Console(new CompactJsonFormatter());
    }
}
