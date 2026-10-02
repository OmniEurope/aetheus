// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;

namespace Aetheus.Front.Components.Logs;

/// <summary>
/// Recette R-454: one row of the Performance page's Markdown export. The three tables do not share their
/// columns, so each row says which table it comes from and leaves the columns of the others empty.
/// </summary>
public sealed record PerformanceExportRow(
    string Table,
    string Method,
    string Route,
    int? StatusCode = null,
    double? DurationMs = null,
    int? Count = null,
    double? P50Ms = null,
    double? P95Ms = null,
    double? P99Ms = null,
    double? MaxMs = null,
    long? Bytes = null,
    DateTime? At = null);

/// <summary>
/// Recette R-454: the administration Performance page as a Markdown file for an AI: no analysis, all of
/// the content. Every slowest call and every route of the server report, then every call this browser
/// session kept. Durations in milliseconds, times in UTC, numbers culture-invariant.
/// </summary>
public static class PerformanceMarkdownExport
{
    public static OmniMarkdownTableExport<PerformanceExportRow> Create(
        ApiPerformanceReportDto report,
        IReadOnlyList<ClientApiCall> browserCalls,
        Func<string, string> text)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(browserCalls);
        ArgumentNullException.ThrowIfNull(text);
        var rows = Rows(report, browserCalls, text);
        return new OmniMarkdownTableExport<PerformanceExportRow>
        {
            Title = text("PerformanceExportTitle"),
            Fields =
            [
                new(text("PerformanceExportSince"), report.Since is { } since ? IsoUtc(since) : text("ExportNone")),
                new(text("PerformanceExportSamples"), report.SampleCount.ToString(CultureInfo.InvariantCulture)),
                new(text("PerformanceExportTruncated"), text(report.Truncated ? "Yes" : "No"))
            ],
            Columns =
            [
                new(text("PerformanceExportTable"), row => row.Table),
                new(text("Method"), row => row.Method),
                new(text("Route"), row => row.Route),
                new(text("Status"), row => Invariant(row.StatusCode)),
                new(string.Concat(text("Duration"), " (ms)"), row => Invariant(row.DurationMs)),
                new(text("PerformanceCalls"), row => Invariant(row.Count)),
                new(string.Concat(text("PerformanceP50"), " (ms)"), row => Invariant(row.P50Ms)),
                new(string.Concat(text("PerformanceP95"), " (ms)"), row => Invariant(row.P95Ms)),
                new(string.Concat(text("PerformanceP99"), " (ms)"), row => Invariant(row.P99Ms)),
                new(string.Concat(text("Maximum"), " (ms)"), row => Invariant(row.MaxMs)),
                new(string.Concat(text("Size"), " (B)"), row => Invariant(row.Bytes)),
                new(string.Concat(text("Date"), " (UTC)"), row => row.At is { } at ? IsoUtc(at) : null)
            ],
            RowLimit = Math.Max(rows.Count, 1),
            PageSize = Math.Max(rows.Count, 1),
            EmptyText = text("PerformanceNoSamples"),
            LoadPage = request => Task.FromResult(new OmniDataGridResult<PerformanceExportRow>(
                [.. rows.Skip(request.Skip).Take(request.PageSize)], rows.Count))
        };
    }

    public static IReadOnlyList<PerformanceExportRow> Rows(
        ApiPerformanceReportDto report,
        IReadOnlyList<ClientApiCall> browserCalls,
        Func<string, string> text)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(browserCalls);
        ArgumentNullException.ThrowIfNull(text);
        var slowest = text("PerformanceSlowestCalls");
        var byRoute = text("PerformanceByRoute");
        var browser = text("PerformanceThisBrowser");
        return
        [
            .. report.Slowest.Select(call => new PerformanceExportRow(
                slowest, call.Method, call.Route, StatusCode: call.StatusCode, DurationMs: call.DurationMs, At: call.At)),
            .. report.Endpoints.Select(endpoint => new PerformanceExportRow(
                byRoute, endpoint.Method, endpoint.Route, Count: endpoint.Count, P50Ms: endpoint.P50Ms,
                P95Ms: endpoint.P95Ms, P99Ms: endpoint.P99Ms, MaxMs: endpoint.MaxMs)),
            .. browserCalls.OrderByDescending(call => call.DurationMs).Select(call => new PerformanceExportRow(
                browser, call.Method, call.Path, StatusCode: call.StatusCode, DurationMs: call.DurationMs,
                Bytes: call.Bytes, At: call.At))
        ];
    }

    private static string? Invariant(double? value) => value?.ToString("0.###", CultureInfo.InvariantCulture);

    private static string? Invariant(long? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static string? Invariant(int? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static string IsoUtc(DateTime value) => Shared.AppTelemetryMarkdownExport.IsoUtc(value);
}
