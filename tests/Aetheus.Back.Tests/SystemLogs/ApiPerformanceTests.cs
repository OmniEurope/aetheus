// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aetheus.Back.Components.SystemLogs;
using Aetheus.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Aetheus.Back.Tests.SystemLogs;

/// <summary>
/// PLAN-003 lot 27 / D27, R-455: the API's own timings, measured by the Aetheus.Telemetry recorder (whose
/// arithmetic is pinned in Aetheus.Telemetry.Tests). Here: what Aetheus keeps, how the report maps to the
/// page's contract, and the one thing a unit test cannot prove: that ASP.NET Core really publishes the
/// request duration, with the route TEMPLATE, to the in-process listener when no exporter is configured.
/// </summary>
public sealed class ApiPerformanceTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly DateTime Now = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

    private static RequestPerformanceRecorder AetheusRecorder(TimeProvider clock)
    {
        var options = new RequestPerformanceOptions();
        ApiPerformanceReport.Configure(options);
        return new RequestPerformanceRecorder(Options.Create(options), clock);
    }

    [Fact]
    public void Recorder_KeepsOnlyApiRoutes_AndForgetsSamplesOlderThan24Hours()
    {
        var clock = new FakeTimeProvider(Now);
        using var recorder = AetheusRecorder(clock);

        recorder.Record("api/projects", "GET", 200, 12);
        recorder.Record("{*path:nonfile}", "GET", 200, 3); // the SPA fallback
        recorder.Record(null, "GET", 404, 1);              // no endpoint matched
        clock.Advance(TimeSpan.FromHours(25));
        recorder.Record("api/servers", "GET", 200, 40);

        var kept = Assert.Single(recorder.Snapshot());
        Assert.Equal("api/servers", kept.Route);
    }

    [Fact]
    public void Report_MapsThePackageSummary_ToThePageContract()
    {
        var samples = Enumerable.Range(1, 30)
            .Select(i => new RequestTimingSample(Now.AddMinutes(-i), "GET", "api/projects", 200, i))
            .Append(new RequestTimingSample(Now, "POST", "api/pipelines/{id}", 201, 900))
            .ToList();

        var report = ApiPerformanceReport.ToDto(RequestPerformanceReport.Build(samples, truncated: true));

        Assert.Equal(31, report.SampleCount);
        Assert.True(report.Truncated);
        Assert.Equal(Now.AddMinutes(-30), report.Since);
        var slowest = report.Slowest[0];
        Assert.Equal(("POST", "api/pipelines/{id}", 201, 900d, Now),
            (slowest.Method, slowest.Route, slowest.StatusCode, slowest.DurationMs, slowest.At));
        var projects = report.Endpoints.Single(endpoint => endpoint.Route == "api/projects");
        Assert.Equal((30, 15d, 29d, 30d, 30d),
            (projects.Count, projects.P50Ms, projects.P95Ms, projects.P99Ms, projects.MaxMs));
    }

    [Fact]
    public async Task ARealRequest_IsHeardByTheRecorder_UnderItsRouteTemplate()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CustomWebApplicationFactory.GenerateTestToken());
        var ct = TestContext.Current.CancellationToken;

        (await client.GetAsync("/api/admin/performance", ct)).EnsureSuccessStatusCode();
        // The first call is itself measured; the second report sees it.
        var response = await client.GetAsync("/api/admin/performance", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = await response.Content.ReadFromJsonAsync<ApiPerformanceReportDto>(TestJsonOptions.Default, ct);
        var endpoint = Assert.Single(report!.Endpoints, item => item.Route == "api/admin/performance");
        Assert.Equal("GET", endpoint.Method);
        Assert.True(endpoint.Count >= 1);
        Assert.Same(
            factory.Services.GetRequiredService<RequestPerformanceRecorder>(),
            factory.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<RequestPerformanceRecorder>().Single());
    }
}
