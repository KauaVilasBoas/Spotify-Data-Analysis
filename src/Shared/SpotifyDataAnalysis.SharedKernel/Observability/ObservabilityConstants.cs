namespace SpotifyDataAnalysis.SharedKernel.Observability;

/// <summary>
/// Canonical names for the cross-cutting observability surface: the correlation-id header/log property and
/// the Serilog application enrichment. Centralized here — in the pure <c>SharedKernel</c> — so the Host
/// middleware, the CQRS <c>LoggingBehavior</c>, the Serilog wiring and every test refer to the same symbol
/// instead of repeating magic strings that must stay in lockstep.
/// </summary>
public static class ObservabilityConstants
{
    /// <summary>Inbound/outbound HTTP header carrying the correlation id (RFC-style custom header).</summary>
    public const string CorrelationIdHeader = "X-Correlation-Id";

    /// <summary>Structured-log property name the correlation id is emitted under.</summary>
    public const string CorrelationIdProperty = "CorrelationId";

    /// <summary>Serilog enrichment property identifying the emitting application in the log aggregator.</summary>
    public const string ApplicationProperty = "Application";

    /// <summary>Value of <see cref="ApplicationProperty"/> for the SpotifyDataAnalysis Host.</summary>
    public const string ApplicationName = "SpotifyDataAnalysis.Api";
}
