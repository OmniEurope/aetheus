// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using Aetheus.Front.Components.Logs;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages.Logs;

/// <summary>PLAN-003 lot 27 / D27: the Performance page and the browser-side timing it shows.</summary>
public sealed class PerformanceTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public PerformanceTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        Services.AddSingleton<ClientApiTimings>();
    }

    [Theory]
    [InlineData("http://test/api/pipelines/51/runs?page=2", "api/pipelines/{id}/runs")]
    [InlineData("http://test/api/git/commits/0123456789abcdef0123456789abcdef01234567", "api/git/commits/{id}")]
    [InlineData("http://test/api/vaults/3fa85f64-5717-4562-b3fc-2c963f66afa6/secrets", "api/vaults/{id}/secrets")]
    [InlineData("http://test/api/projects", "api/projects")]
    public void BrowserPaths_DropIdentifiersAndQuery(string url, string expected) =>
        Assert.Equal(expected, ClientApiTimings.Normalize(new Uri(url)));

    [Fact]
    public void TheSessionStore_KeepsOnlyTheLastCalls()
    {
        var timings = new ClientApiTimings();
        for (var i = 0; i < ClientApiTimings.Capacity + 5; i++)
            timings.Record(new ClientApiCall(DateTime.UtcNow, "GET", $"api/{i}", 200, i, null));

        var kept = timings.Snapshot();
        Assert.Equal(ClientApiTimings.Capacity, kept.Count);
        Assert.Equal("api/5", kept[0].Path);
    }

    [Fact]
    public async Task TheHandler_KeepsACallThatGotNoAnswer_WithStatusZero()
    {
        var timings = new ClientApiTimings();
        using var invoker = new HttpMessageInvoker(new ClientApiTimingHandler(timings)
        {
            InnerHandler = new ThrowingHandler(new HttpRequestException("unreachable"))
        });

        await Assert.ThrowsAsync<HttpRequestException>(() => invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://test/api/pipelines/51/runs?page=2"),
            Xunit.TestContext.Current.CancellationToken));

        var call = Assert.Single(timings.Snapshot());
        Assert.Equal("api/pipelines/{id}/runs", call.Path);
        Assert.Equal(0, call.StatusCode);
        Assert.Null(call.Bytes);
    }

    [Fact]
    public async Task TheHandler_LeavesOutACallThePageCancelled()
    {
        var timings = new ClientApiTimings();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        using var invoker = new HttpMessageInvoker(new ClientApiTimingHandler(timings)
        {
            InnerHandler = new ThrowingHandler(new TaskCanceledException())
        });

        await Assert.ThrowsAsync<TaskCanceledException>(() => invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://test/api/projects"), cancelled.Token));

        Assert.Empty(timings.Snapshot());
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(exception);
    }

    [Fact]
    public void ThePage_ShowsTheServerWindow_TheRoutes_AndThisBrowsersSlowestCalls()
    {
        _handler.SetJsonResponse("api/admin/performance", new ApiPerformanceReportDto
        {
            Since = new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc),
            SampleCount = 1234,
            Slowest = [new ApiCallTimingDto { Method = "GET", Route = "api/analysis/findings/{id}/occurrences", StatusCode = 200, DurationMs = 4200 }],
            Endpoints = [new ApiEndpointTimingDto { Method = "GET", Route = "api/analysis/findings/{id}/occurrences", Count = 9, P50Ms = 900, P95Ms = 4000, P99Ms = 4200, MaxMs = 4200 }]
        });
        Services.GetRequiredService<ClientApiTimings>().Record(
            new ClientApiCall(DateTime.UtcNow, "GET", "api/pipelines/{id}/runs", 200, 850, 2048));

        var cut = Render<Performance>();

        cut.WaitForAssertion(() => Assert.Contains("api/analysis/findings/{id}/occurrences", cut.Markup, StringComparison.Ordinal));
        Assert.Contains("PerformanceWindow", cut.Find("[data-testid='performance-window']").TextContent, StringComparison.Ordinal);
        Assert.Contains("4.20 s", cut.Markup.Replace(',', '.'), StringComparison.Ordinal);
        Assert.Contains("api/pipelines/{id}/runs", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task R181_NoRefreshButton_AnApiPerformancePushReloadsTheReport()
    {
        _handler.SetJsonResponse("api/admin/performance", new ApiPerformanceReportDto
        {
            Since = new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc),
            SampleCount = 1,
            Slowest = [new ApiCallTimingDto { Method = "GET", Route = "api/projects", StatusCode = 200, DurationMs = 10 }]
        });
        var cut = Render<Performance>();
        cut.WaitForAssertion(() => Assert.Contains("api/projects", cut.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("Refresh", StringComparison.Ordinal));
        _handler.SetJsonResponse("api/admin/performance", new ApiPerformanceReportDto
        {
            Since = new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc),
            SampleCount = 2,
            Slowest = [new ApiCallTimingDto { Method = "POST", Route = "api/pipelines/{id}/runs", StatusCode = 201, DurationMs = 900 }]
        });

        await cut.InvokeAsync(() => cut.Instance.ReloadFromPushAsync());

        Assert.Contains("api/pipelines/{id}/runs", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task R181_AFailedPushReload_KeepsTheFiguresOnScreen()
    {
        _handler.SetJsonResponse("api/admin/performance", new ApiPerformanceReportDto
        {
            Since = new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc),
            SampleCount = 1,
            Slowest = [new ApiCallTimingDto { Method = "GET", Route = "api/projects", StatusCode = 200, DurationMs = 10 }]
        });
        var cut = Render<Performance>();
        cut.WaitForAssertion(() => Assert.Contains("api/projects", cut.Markup, StringComparison.Ordinal));
        _handler.SetResponse(HttpMethod.Get, "api/admin/performance", HttpStatusCode.InternalServerError);

        await cut.InvokeAsync(() => cut.Instance.ReloadFromPushAsync());

        Assert.Contains("api/projects", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("GridLoadFailed", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void R454_TheThreeTables_AreTabs_AndTheWholePageExportsToMarkdown()
    {
        _handler.SetJsonResponse("api/admin/performance", new ApiPerformanceReportDto
        {
            Since = new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc),
            SampleCount = 1,
            Slowest = [new ApiCallTimingDto { Method = "GET", Route = "api/projects", StatusCode = 200, DurationMs = 10 }]
        });

        var cut = Render<Performance>();

        cut.WaitForAssertion(() => Assert.Contains("api/projects", cut.Markup, StringComparison.Ordinal));
        var tabs = cut.FindAll("[role='tab']");
        Assert.Equal(["slowest", "routes", "browser"], tabs.Select(tab => tab.GetAttribute("data-key")).ToArray());
        Assert.Contains(cut.FindAll("button"), button => button.TextContent.Contains("PerformanceExportMarkdown", StringComparison.Ordinal));
    }

    [Fact]
    public void R454_TheExport_HoldsEveryRowOfTheThreeTables_BrowserCallsIncludedBeyondTheTwentyShown()
    {
        var report = new ApiPerformanceReportDto
        {
            Since = new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc),
            SampleCount = 3,
            Slowest = [new ApiCallTimingDto { Method = "GET", Route = "api/projects", StatusCode = 200, DurationMs = 4200.5, At = new DateTime(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc) }],
            Endpoints = [new ApiEndpointTimingDto { Method = "GET", Route = "api/projects", Count = 3, P50Ms = 10, P95Ms = 4200.5, P99Ms = 4200.5, MaxMs = 4200.5 }]
        };
        var browser = Enumerable.Range(1, 25)
            .Select(i => new ClientApiCall(new DateTime(2026, 9, 10, 9, 0, i, DateTimeKind.Utc), "GET", $"api/call/{i}", 200, i, 100))
            .ToList();

        var rows = PerformanceMarkdownExport.Rows(report, browser, key => key);
        var export = PerformanceMarkdownExport.Create(report, browser, key => key);

        Assert.Equal(27, rows.Count);
        Assert.Equal(["PerformanceSlowestCalls", "PerformanceByRoute"], rows.Take(2).Select(row => row.Table).ToArray());
        Assert.Equal("api/call/25", rows[2].Route);                       // the slowest browser call first
        Assert.Equal(25, rows.Count(row => row.Table == "PerformanceThisBrowser"));
        Assert.Equal("4200.5", export.Columns.Single(column => column.Title == "Duration (ms)").Value(rows[0]));
        Assert.Equal("2026-09-10T09:00:00.000Z", export.Columns.Single(column => column.Title == "Date (UTC)").Value(rows[0]));
        Assert.Null(export.Columns.Single(column => column.Title == "PerformanceCalls").Value(rows[0]));
    }

    [Fact]
    public void AFailedLoad_SaysSo()
    {
        _handler.SetResponse(HttpMethod.Get, "api/admin/performance", HttpStatusCode.Forbidden);

        var cut = Render<Performance>();

        cut.WaitForAssertion(() => Assert.Contains("GridLoadFailed", cut.Markup, StringComparison.Ordinal));
    }
}
