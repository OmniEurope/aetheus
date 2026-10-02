// SPDX-License-Identifier: EUPL-1.2
using Microsoft.Extensions.Options;

namespace Aetheus.Telemetry.Tests;

/// <summary>
/// R-455: the request timings the package measures in process and exports per route. The arithmetic is
/// pinned on its own; the recorder's filters and the exported gauges are checked through their public
/// surface, with a clock the test moves. The registration through AddAetheusTelemetry is tested with
/// the other registration tests (they share the AETHEUS_TELEMETRY_ENABLED variable).
/// </summary>
public sealed class RequestPerformanceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Percentiles_AreNearestRank_SoEveryFigureIsARealRequest()
    {
        double[] ascending = [10, 20, 30, 40, 50, 60, 70, 80, 90, 1000];

        Assert.Equal(50, RequestPerformanceReport.Percentile(ascending, 0.50));
        Assert.Equal(1000, RequestPerformanceReport.Percentile(ascending, 0.95));
        Assert.Equal(10, RequestPerformanceReport.Percentile([10], 0.99));
        Assert.Equal(0, RequestPerformanceReport.Percentile([], 0.99));
    }

    [Fact]
    public void Report_RanksRoutesByTheir95thPercentile_AndKeepsTheTwentySlowestCalls()
    {
        var at = Now.UtcDateTime;
        var samples = Enumerable.Range(1, 30)
            .Select(i => new RequestTimingSample(at.AddMinutes(-i), "GET", "api/projects", 200, i))
            .Append(new RequestTimingSample(at, "GET", "api/pipelines/{id}", 200, 900))
            .ToList();

        var report = RequestPerformanceReport.Build(samples, truncated: false);

        Assert.Equal(31, report.SampleCount);
        Assert.False(report.Truncated);
        Assert.Equal(RequestPerformanceReport.SlowestCount, report.Slowest.Count);
        Assert.Equal(900, report.Slowest[0].DurationMs);
        Assert.Equal("api/pipelines/{id}", report.Routes[0].Route);
        var projects = report.Routes.Single(route => route.Route == "api/projects");
        Assert.Equal((30, 15d, 29d, 30d, 30d), (projects.Count, projects.P50Ms, projects.P95Ms, projects.P99Ms, projects.MaxMs));
        Assert.Equal(at.AddMinutes(-30), report.Since);
    }

    [Fact]
    public void Recorder_ByDefault_KeepsEveryTemplatedRouteButTheProbesAndStaticAssets()
    {
        var clock = new ManualClock(Now);
        using var recorder = new RequestPerformanceRecorder(Options.Create(new RequestPerformanceOptions()), clock);

        recorder.Record("orders/{id}", "GET", 200, 12);
        recorder.Record("health/ready", "GET", 200, 1);
        recorder.Record("_framework/blazor.web.js", "GET", 200, 1);
        recorder.Record(null, "GET", 404, 1);

        var kept = Assert.Single(recorder.Snapshot());
        Assert.Equal(("GET", "orders/{id}", 200, 12d), (kept.Method, kept.Route, kept.StatusCode, kept.DurationMs));
        Assert.Equal(Now.UtcDateTime, kept.At);
    }

    [Fact]
    public void Recorder_ForgetsSamplesOlderThanTheWindow()
    {
        var clock = new ManualClock(Now);
        using var recorder = new RequestPerformanceRecorder(Options.Create(new RequestPerformanceOptions()), clock);

        recorder.Record("orders", "GET", 200, 5);
        clock.Advance(RequestPerformanceRecorder.Window + TimeSpan.FromMinutes(1));
        recorder.Record("orders/{id}", "GET", 200, 7);

        Assert.Equal("orders/{id}", Assert.Single(recorder.Snapshot()).Route);
    }

    [Fact]
    public async Task Recorder_AppliesTheHostFilter_AndKeepsQuietRoutesOutOfTheChangeSignal()
    {
        var options = new RequestPerformanceOptions { RouteFilter = route => route.StartsWith("api/", StringComparison.Ordinal) };
        options.QuietRoutes.Add("api/report");
        using var recorder = new RequestPerformanceRecorder(Options.Create(options), new ManualClock(Now));

        recorder.Record("orders", "GET", 200, 5);
        recorder.Record("api/report", "GET", 200, 5);

        var change = recorder.WaitForChangeAsync(TestContext.Current.CancellationToken);
        Assert.False(change.IsCompleted);
        Assert.Equal("api/report", Assert.Single(recorder.Snapshot()).Route);

        recorder.Record("api/orders", "GET", 200, 5);
        await change.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Metrics_ExportOneSeriesPerRouteAndMethod_WithTheReportFigures()
    {
        var clock = new ManualClock(Now);
        using var recorder = new RequestPerformanceRecorder(Options.Create(new RequestPerformanceOptions()), clock);
        using var metrics = new RequestPerformanceMetrics(recorder, clock);
        foreach (var duration in new double[] { 10, 20, 30, 40 })
            recorder.Record("orders/{id}", "GET", 200, duration);
        recorder.Record("orders/{id}", "DELETE", 204, 7);

        var p95 = metrics.Measure(route => route.P95Ms).ToList();
        var count = metrics.Measure(route => route.Count).ToList();

        Assert.Equal(2, p95.Count);
        var get = p95.Single(measurement => Tag(measurement, RequestPerformanceMetrics.MethodAttribute) == "GET");
        Assert.Equal(40, get.Value);
        Assert.Equal("orders/{id}", Tag(get, RequestPerformanceMetrics.RouteAttribute));
        Assert.Equal(4, count.Single(measurement => Tag(measurement, RequestPerformanceMetrics.MethodAttribute) == "GET").Value);
        Assert.Equal(1, count.Single(measurement => Tag(measurement, RequestPerformanceMetrics.MethodAttribute) == "DELETE").Value);
    }

    [Fact]
    public void R476_ARouteWrittenInTwoCases_IsOneLine()
    {
        var clock = new ManualClock(Now);
        using var recorder = new RequestPerformanceRecorder(Options.Create(new RequestPerformanceOptions()), clock);
        recorder.Record("api/AppMonitoring/apps/{id}", "GET", 200, 10);
        recorder.Record("api/appmonitoring/apps/{id}", "GET", 200, 30);

        var route = Assert.Single(RequestPerformanceReport.Build(recorder.Snapshot(), truncated: false).Routes);

        Assert.Equal(("api/appmonitoring/apps/{id}", 2), (route.Route, route.Count));
    }

    [Fact]
    public void R476_TheWindowGauge_SaysHowLongTheKeptRequestsSpan()
    {
        var clock = new ManualClock(Now);
        using var recorder = new RequestPerformanceRecorder(Options.Create(new RequestPerformanceOptions()), clock);
        using var metrics = new RequestPerformanceMetrics(recorder, clock);

        Assert.Empty(metrics.MeasureWindow()); // nothing measured yet: no window to state

        recorder.Record("orders", "GET", 200, 10);
        clock.Advance(TimeSpan.FromMinutes(90));
        recorder.Record("orders", "GET", 200, 12);

        Assert.Equal(90 * 60, metrics.MeasureWindow().Single().Value);
    }

    [Fact]
    public void Metrics_ReadOneReportPerCollection_ThenANewOneOnceTheClockMoves()
    {
        var clock = new ManualClock(Now);
        using var recorder = new RequestPerformanceRecorder(Options.Create(new RequestPerformanceOptions()), clock);
        using var metrics = new RequestPerformanceMetrics(recorder, clock);
        recorder.Record("orders", "GET", 200, 10);

        Assert.Single(metrics.Measure(route => route.MaxMs));
        recorder.Record("orders", "GET", 200, 90);
        // Same collection (the clock did not move): the five gauges share the report already built.
        Assert.Equal(10, metrics.Measure(route => route.MaxMs).Single().Value);

        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(90, metrics.Measure(route => route.MaxMs).Single().Value);
    }

    private static string? Tag(System.Diagnostics.Metrics.Measurement<double> measurement, string key)
    {
        foreach (var tag in measurement.Tags)
        {
            if (tag.Key == key) return tag.Value as string;
        }
        return null;
    }

    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
