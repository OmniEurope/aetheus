// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Extensions;

/// <summary>
/// Builds the application's Content-Security-Policy and registers a middleware that emits the
/// standard set of security headers. Centralised here so <c>Program.cs</c> stays focused on
/// host wiring.
/// </summary>
internal static class SecurityHeadersExtensions
{
    public static WebApplication UseAetheusSecurityHeaders(
        this WebApplication app,
        IEnumerable<string> corsOrigins)
    {
        // Content-Security-Policy - Blazor WASM requires 'wasm-unsafe-eval' and Monaco loads worker
        // scripts from blob: URLs. Recette R-526: styles are 'self' only. The 'unsafe-inline' this header
        // carried dated from the component library the front no longer uses; the API serves no page of
        // its own, and the front's policy is the static server's.
        // connect-src is restricted to self + the configured frontend origin (translated to ws/wss
        // for SignalR). No global ws:/wss: wildcard. Computed once at startup since CORS origins
        // don't change at runtime.
        var cspConnectSources = new List<string> { "'self'" };
        foreach (var origin in corsOrigins.Distinct())
        {
            if (Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
            {
                cspConnectSources.Add(origin);
                var wsScheme = originUri.Scheme == "https" ? "wss" : "ws";
                cspConnectSources.Add($"{wsScheme}://{originUri.Authority}");
            }
        }
        var contentSecurityPolicy =
            "default-src 'self'; " +
            "script-src 'self' 'wasm-unsafe-eval' blob:; " +
            "worker-src 'self' blob:; " +
            "style-src 'self'; " +
            "img-src 'self' data: blob:; " +
            "font-src 'self' data:; " +
            $"connect-src {string.Join(' ', cspConnectSources.Distinct())}; " +
            "frame-ancestors 'none'; " +
            "base-uri 'self'; " +
            "form-action 'self'; " +
            // PLAN-003 lot 14: the browser names the refused script for us. report-uri is deprecated
            // but still the only one Safari and older Chrome honour, so both are sent.
            "report-uri /api/security/csp-report; " +
            "report-to csp-endpoint";

        // The Reporting API endpoint the report-to directive above refers to.
        const string reportingEndpoints = "csp-endpoint=\"/api/security/csp-report\"";

        app.Use(async (context, next) =>
        {
            context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
            context.Response.Headers.Append("X-Frame-Options", "DENY");
            context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
            context.Response.Headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
            context.Response.Headers.Append("Reporting-Endpoints", reportingEndpoints);
            context.Response.Headers.Append("Content-Security-Policy", contentSecurityPolicy);
            await next();
        });

        return app;
    }
}
