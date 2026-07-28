namespace SpotifyDataAnalysis.SharedKernel.Http;

/// <summary>
/// Canonical HTTP-layer literals shared across the Host and modules: response content types and the
/// baseline security response headers. Centralized so the wire contract lives in one place and the
/// middleware, configuration and tests cannot drift apart.
/// </summary>
public static class HttpConstants
{
    /// <summary>Response content types emitted directly (outside MVC's negotiated formatters).</summary>
    public static class ContentTypes
    {
        /// <summary>RFC 7807 problem document media type used by the error boundary.</summary>
        public const string ProblemJson = "application/problem+json";

        /// <summary>CSV media type used by data exports.</summary>
        public const string Csv = "text/csv";
    }

    /// <summary>
    /// Baseline security response headers: defense-in-depth browser hardening applied to every
    /// response by the Host's <c>SecurityHeadersMiddleware</c>.
    /// </summary>
    public static class SecurityHeaders
    {
        public const string ContentTypeOptions = "X-Content-Type-Options";
        public const string ContentTypeOptionsValue = "nosniff";

        public const string FrameOptions = "X-Frame-Options";
        public const string FrameOptionsValue = "DENY";

        public const string ReferrerPolicy = "Referrer-Policy";
        public const string ReferrerPolicyValue = "strict-origin-when-cross-origin";

        public const string PermissionsPolicy = "Permissions-Policy";
        public const string PermissionsPolicyValue = "camera=(), microphone=(), geolocation=()";

        public const string XssProtection = "X-XSS-Protection";
        public const string XssProtectionValue = "0";
    }
}
