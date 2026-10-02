// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Telemetry;

namespace Aetheus.Back.Components.SystemLogs;

/// <summary>
/// PLAN-003 lot 27 / D27, R-455: the administration Performance page reads Aetheus's own request timings
/// from the recorder of the <c>Aetheus.Telemetry</c> package, the same measurement every supervised
/// application exports. This class only says which requests Aetheus keeps and maps the package's report
/// to the page's contract.
/// </summary>
public static class ApiPerformanceReport
{
    /// <summary>The Performance page's own route: reading the report must not announce a change of it,
    /// or an open page would reload itself forever on its own traffic.</summary>
    public const string ReportRoute = "api/admin/performance";

    /// <summary>Only the API is measured: the static files, the SPA fallback and the health probes are not
    /// the API being timed.</summary>
    public static void Configure(RequestPerformanceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.RouteFilter = route => route.StartsWith("api/", StringComparison.OrdinalIgnoreCase);
        options.QuietRoutes.Add(ReportRoute);
    }

    public static ApiPerformanceReportDto Build(RequestPerformanceRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        return ToDto(RequestPerformanceReport.Build(recorder.Snapshot(), recorder.Truncated));
    }

    public static ApiPerformanceReportDto ToDto(RequestPerformanceSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return new ApiPerformanceReportDto
        {
            Since = summary.Since,
            SampleCount = summary.SampleCount,
            Truncated = summary.Truncated,
            Slowest = [.. summary.Slowest.Select(sample => new ApiCallTimingDto
            {
                Method = sample.Method,
                Route = sample.Route,
                StatusCode = sample.StatusCode,
                DurationMs = sample.DurationMs,
                At = sample.At
            })],
            Endpoints = [.. summary.Routes.Select(route => new ApiEndpointTimingDto
            {
                Method = route.Method,
                Route = route.Route,
                Count = route.Count,
                P50Ms = route.P50Ms,
                P95Ms = route.P95Ms,
                P99Ms = route.P99Ms,
                MaxMs = route.MaxMs
            })]
        };
    }
}
