namespace SpotifyDataAnalysis.SharedKernel.Http;

/// <summary>
/// Canonical RFC 7807 <c>type</c> URIs for the error contract. Each URI dereferences (conceptually) to
/// human-readable documentation of the error class; the Host's error boundary stamps one of these on every
/// problem document so clients can branch on a stable identifier rather than the message. Centralized here
/// so the middleware and the tests that assert the wire contract share one source of truth.
/// </summary>
public static class ProblemDetailsTypes
{
    /// <summary>Base URI every problem <c>type</c> is built from.</summary>
    public const string BaseUri = "https://spotifydataanalysis.app/errors/";

    /// <summary>422 — a domain invariant/business rule was violated.</summary>
    public const string BusinessRuleViolation = BaseUri + "business-rule-violation";

    /// <summary>404 — the requested resource does not exist.</summary>
    public const string NotFound = BaseUri + "not-found";

    /// <summary>409 — the request conflicts with the current state of the resource.</summary>
    public const string Conflict = BaseUri + "conflict";

    /// <summary>403 — the caller is authenticated but not allowed to perform the operation.</summary>
    public const string Forbidden = BaseUri + "forbidden";

    /// <summary>400 — one or more request fields failed validation.</summary>
    public const string ValidationError = BaseUri + "validation-error";

    /// <summary>500 — an unexpected, unhandled error occurred.</summary>
    public const string InternalError = BaseUri + "internal-error";
}
