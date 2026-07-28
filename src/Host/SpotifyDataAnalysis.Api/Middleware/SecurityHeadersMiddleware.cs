using SpotifyDataAnalysis.SharedKernel.Http;

namespace SpotifyDataAnalysis.Api.Middleware;

/// <summary>
/// Adds a baseline of security response headers to every response: defense-in-depth browser hardening.
///
/// <list type="bullet">
///   <item><c>X-Content-Type-Options: nosniff</c> — stop MIME-type sniffing.</item>
///   <item><c>X-Frame-Options: DENY</c> — forbid framing (clickjacking).</item>
///   <item><c>Referrer-Policy: strict-origin-when-cross-origin</c> — trim the referrer cross-origin.</item>
///   <item><c>Permissions-Policy</c> — deny camera/microphone/geolocation the app never uses.</item>
///   <item><c>X-XSS-Protection: 0</c> — explicitly disable the legacy, buggy auditor.</item>
/// </list>
///
/// HSTS is intentionally NOT set here — it is applied by <c>UseHsts()</c> outside development, so it is
/// never sent over plain HTTP in local dev. The headers are stamped via <c>OnStarting</c> so they are
/// present even when a downstream component short-circuits the response.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var response = ((HttpContext)state).Response;

            response.Headers[HttpConstants.SecurityHeaders.ContentTypeOptions] =
                HttpConstants.SecurityHeaders.ContentTypeOptionsValue;
            response.Headers[HttpConstants.SecurityHeaders.FrameOptions] =
                HttpConstants.SecurityHeaders.FrameOptionsValue;
            response.Headers[HttpConstants.SecurityHeaders.ReferrerPolicy] =
                HttpConstants.SecurityHeaders.ReferrerPolicyValue;
            response.Headers[HttpConstants.SecurityHeaders.PermissionsPolicy] =
                HttpConstants.SecurityHeaders.PermissionsPolicyValue;
            response.Headers[HttpConstants.SecurityHeaders.XssProtection] =
                HttpConstants.SecurityHeaders.XssProtectionValue;

            return Task.CompletedTask;
        }, context);

        return _next(context);
    }
}
