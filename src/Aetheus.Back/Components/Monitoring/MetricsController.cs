// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;
using System.Text;
using Aetheus.Back.Services.DomainEvents;
using Aetheus.Shared.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aetheus.Back.Components.Monitoring;

/// <summary>
/// Exposes process-internal counters in OpenMetrics / Prometheus text format at <c>GET /metrics</c>.
/// Currently published gauges:
///  - <c>aetheus_backgroundqueue_length</c>: items currently waiting.
///  - <c>aetheus_backgroundqueue_capacity</c>: bounded channel capacity.
///  - <c>aetheus_backgroundqueue_dropped_total</c>: items evicted by DropOldest.
///  - <c>aetheus_backgroundqueue_enqueued_total</c>: items written.
///  - <c>aetheus_backgroundqueue_processed_total</c>: items drained.
/// </summary>
[ApiController]
[Route("metrics")]
[Authorize(Roles = "Admin")]
public class MetricsController(IBackgroundTaskQueue queue) : ControllerBase
{
    [HttpGet]
    [Produces("text/plain")]
    public ContentResult Get()
    {
        // Series names derive from AppConstants.MetricsPrefix so a rebrand is a one-line change (S-TECH-RB4N).
        const string q = AppConstants.MetricsPrefix + "backgroundqueue_";
        var sb = new StringBuilder(512);
        AppendGauge(sb, q + "length", "Items currently waiting in the background task queue.", queue.CurrentLength);
        AppendGauge(sb, q + "capacity", "Bounded capacity of the background task queue.", queue.Capacity);
        AppendCounter(sb, q + "dropped_total", "Items evicted by DropOldest overflow strategy since process start.", queue.Dropped);
        AppendCounter(sb, q + "enqueued_total", "Items enqueued since process start.", queue.Enqueued);
        AppendCounter(sb, q + "processed_total", "Items processed since process start.", queue.Processed);
        return new ContentResult { Content = sb.ToString(), ContentType = "text/plain; version=0.0.4", StatusCode = 200 };
    }

    private static void AppendGauge(StringBuilder sb, string name, string help, long value)
        => AppendMetric(sb, name, help, "gauge", value);

    private static void AppendCounter(StringBuilder sb, string name, string help, long value)
        => AppendMetric(sb, name, help, "counter", value);

    private static void AppendMetric(StringBuilder sb, string name, string help, string type, long value)
    {
        sb.Append("# HELP ").Append(name).Append(' ').AppendLine(help);
        sb.Append("# TYPE ").Append(name).Append(' ').AppendLine(type);
        sb.Append(name).Append(' ').Append(value.ToString(CultureInfo.InvariantCulture)).Append('\n');
    }
}
