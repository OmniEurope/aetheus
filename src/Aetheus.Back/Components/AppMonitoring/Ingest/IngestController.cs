// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Aetheus.Back.Components.AppMonitoring.Ingest;

/// <summary>
/// Public OTLP/HTTP ingestion surface (PLAN-001 phases 2-4). Authenticated by a per-app ingestion key in
/// the <c>x-aetheus-ingest-key</c> header (not JWT). Accepts the OTLP/JSON encoding
/// (<c>OTEL_EXPORTER_OTLP_PROTOCOL=http/json</c>); protobuf is rejected with 415. Rate-limited per key,
/// tight body cap. Telemetry content is never written to the backend logs.
/// </summary>
[ApiController]
[Route("api/ingest/otlp/v1")]
[AllowAnonymous]
[EnableRateLimiting("otlp-ingest")]
[RequestSizeLimit(1024 * 1024)]
public sealed class IngestController(IIngestService ingest) : ControllerBase
{
    private const string KeyHeader = "x-aetheus-ingest-key";

    [HttpPost("metrics")]
    public Task<IActionResult> Metrics(CancellationToken ct) =>
        HandleAsync((appId, doc) => ingest.IngestMetricsAsync(appId, OtlpJsonParser.ParseMetrics(doc), ct), "rejectedDataPoints", ct);

    [HttpPost("logs")]
    public Task<IActionResult> Logs(CancellationToken ct) =>
        HandleAsync((appId, doc) => ingest.IngestLogsAsync(appId, OtlpJsonParser.ParseLogs(doc), ct), "rejectedLogRecords", ct);

    [HttpPost("traces")]
    public Task<IActionResult> Traces(CancellationToken ct) =>
        HandleAsync((appId, doc) => ingest.IngestErrorsAsync(appId, OtlpJsonParser.ParseErrors(doc), ct), "rejectedSpans", ct);

    private async Task<IActionResult> HandleAsync(Func<int, JsonDocument, Task<IngestOutcome>> handler, string rejectedField, CancellationToken ct)
    {
        var key = Request.Headers[KeyHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(key))
            return Unauthorized();

        var appId = await ingest.ResolveAppIdAsync(key, ct);
        if (appId is null)
            return Unauthorized();

        var contentType = Request.ContentType ?? string.Empty;
        if (!contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType,
                "Only OTLP/JSON is supported. Set OTEL_EXPORTER_OTLP_PROTOCOL=http/json.");

        JsonDocument doc;
        try
        {
            doc = await JsonDocument.ParseAsync(Request.Body, cancellationToken: ct);
        }
        catch (JsonException)
        {
            return BadRequest("Malformed OTLP/JSON payload.");
        }

        using (doc)
        {
            var outcome = await handler(appId.Value, doc);
            // OTLP success response: empty object, or partialSuccess when caps dropped part of the batch.
            if (outcome.Dropped > 0)
                return Ok(new Dictionary<string, object>
                {
                    ["partialSuccess"] = new Dictionary<string, object>
                    {
                        [rejectedField] = outcome.Dropped,
                        ["errorMessage"] = "Some data was dropped by ingestion caps."
                    }
                });
            return Ok(new { });
        }
    }
}
