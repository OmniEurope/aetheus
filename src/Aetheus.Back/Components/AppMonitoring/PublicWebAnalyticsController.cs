// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.RateLimiting;

namespace Aetheus.Back.Components.AppMonitoring;

[ApiController]
[Route("api/ingest/web-analytics/v1/public/{siteId}")]
[AllowAnonymous]
[EnableRateLimiting("web-analytics-public")]
[RequestSizeLimit(4096)]
public sealed class PublicWebAnalyticsController(
    IAppWebAnalyticsConfigurationService configuration,
    IAppWebAnalyticsService analytics,
    AppAnalyticsSiteRateLimiter siteRateLimiter) : ControllerBase
{
    [HttpOptions]
    public async Task<IActionResult> Options(string siteId, CancellationToken ct)
    {
        if (Request.Headers["Sec-GPC"] == "1" || Request.Headers["DNT"] == "1")
            return NoContent();
        var origin = ResolveOrigin();
        if (string.IsNullOrWhiteSpace(origin))
            return Forbid();
        var context = await configuration.ResolvePublicContextAsync(siteId, origin, ct).ConfigureAwait(false);
        if (context is null)
            return Forbid();
        AddCorsHeaders(origin);
        return NoContent();
    }

    [HttpPost]
    public async Task<IActionResult> Collect(
        string siteId,
        [FromBody] PublicWebAnalyticsEventRequest request,
        CancellationToken ct)
    {
        if (Request.Headers["Sec-GPC"] == "1" || Request.Headers["DNT"] == "1")
            return NoContent();
        var origin = ResolveOrigin();
        if (string.IsNullOrWhiteSpace(origin))
            return Forbid();
        var publicContext = await configuration.ResolvePublicContextAsync(siteId, origin, ct).ConfigureAwait(false);
        if (publicContext is null)
            return Forbid();
        if (!siteRateLimiter.TryAcquire(publicContext.Value.App.Id))
            return StatusCode(StatusCodes.Status429TooManyRequests);
        AddCorsHeaders(origin);
        var userAgent = Request.Headers.UserAgent.ToString();
        if (IsBot(userAgent))
            return NoContent();

        AppWebAnalyticsIngestEvent prepared;
        try
        {
            prepared = PublicWebAnalyticsPseudonymizer.Create(
                publicContext.Value.App,
                publicContext.Value.PseudonymizationKey,
                request,
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        }
        catch (InvalidOperationException)
        {
            return UnprocessableEntity();
        }

        var outcome = await analytics.IngestAsync(
            publicContext.Value.App.Id,
            [prepared],
            ct).ConfigureAwait(false);
        return outcome.Replayed > 0 ? Conflict() : Accepted();
    }

    /// <summary>
    /// A cross-origin <c>sendBeacon</c>/<c>fetch keepalive</c> POST always carries an <c>Origin</c> header,
    /// but a same-origin one (host app served the snippet from the same site as the ingest endpoint) is not
    /// guaranteed to. Falling back to the <c>Referer</c>'s authority (no same-site check here: the
    /// per-app allow-list below remains the actual authorization boundary) avoids silently dropping
    /// those legitimate events while still failing closed when neither header is present.
    /// </summary>
    private string? ResolveOrigin()
    {
        var origin = Request.Headers.Origin.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(origin))
            return origin;

        var referer = Request.Headers.Referer.FirstOrDefault();
        if (Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
            return refererUri.GetLeftPart(UriPartial.Authority);

        return null;
    }

    private void AddCorsHeaders(string origin)
    {
        Response.Headers.AccessControlAllowOrigin = origin;
        Response.Headers.AccessControlAllowMethods = "POST, OPTIONS";
        Response.Headers.AccessControlAllowHeaders = "content-type";
        Response.Headers.Vary = "Origin";
    }

    private static bool IsBot(string userAgent) =>
        userAgent.Contains("bot", StringComparison.OrdinalIgnoreCase)
        || userAgent.Contains("crawler", StringComparison.OrdinalIgnoreCase)
        || userAgent.Contains("spider", StringComparison.OrdinalIgnoreCase)
        || userAgent.Contains("headless", StringComparison.OrdinalIgnoreCase)
        || userAgent.Contains("lighthouse", StringComparison.OrdinalIgnoreCase)
        || userAgent.Contains("uptime", StringComparison.OrdinalIgnoreCase);
}
