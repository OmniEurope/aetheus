// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using Aetheus.Front.Shared;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;

namespace Aetheus.Front.Tests.Shared;

public class AppTelemetryPanelTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public AppTelemetryPanelTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    private void SeedTelemetry()
    {
        _handler.SetJsonResponse("api/appmonitoring/projects/1/apps", new List<MonitoredAppDto>
        {
            new() { Id = 5, ProjectId = 1, Name = "toto-qa", CurrentStatus = AppHealthStatus.Up, HasIngestKey = true }
        });
        _handler.SetJsonResponse("metrics/names", new List<string> { "http.server.request.duration" });
        _handler.SetJsonResponse("thresholds", new List<AppMetricThresholdDto>());
        _handler.SetJsonResponse("/logs", new PaginatedResult<AppLogEntryDto>());
        _handler.SetJsonResponse("/errors", new PaginatedResult<AppErrorEventDto>());
        _handler.SetJsonResponse("metrics/series", new MetricSeriesDto { MetricName = "http.server.request.duration" });
        _handler.SetJsonResponse("/web-analytics?days=30", new AppWebAnalyticsSummaryDto
        {
            UniqueVisitorsToday = 3,
            UniqueVisitorsThisWeek = 8,
            UniqueVisitorsThisMonth = 12,
            StorageBudgetBytes = 104_857_600
        });
        _handler.SetJsonResponse(
            "web-analytics/configuration",
            new AppWebAnalyticsConfigurationDto
            {
                SiteId = "toto-qa",
                AllowedOrigins = ["https://toto.example"],
                StorageBudgetBytes = 104_857_600
            });
    }

    [Fact]
    public void RendersAppPicker_AndTelemetrySubTabs()
    {
        SeedTelemetry();
        var cut = Render<AppTelemetryPanel>(p => p.Add(c => c.ProjectId, 1));
        cut.WaitForState(() => cut.Markup.Contains("toto-qa"), TimeSpan.FromSeconds(3));

        Assert.Contains("toto-qa", cut.Markup);        // app picker populated
        Assert.Contains("Visitors", cut.Markup);
        Assert.Contains("Metrics", cut.Markup);        // sub-tab labels (stub localizer echoes keys)
        Assert.Contains("Logs", cut.Markup);
        Assert.Contains("Errors", cut.Markup);
        Assert.Contains("Ingestion", cut.Markup);
    }

    [Fact]
    public void EmptyProject_ShowsNoAppsState()
    {
        _handler.SetJsonResponse("api/appmonitoring/projects/1/apps", new List<MonitoredAppDto>());
        var cut = Render<AppTelemetryPanel>(p => p.Add(c => c.ProjectId, 1));
        cut.WaitForState(() => !cut.Markup.Contains("rz-progressbar-circular"), TimeSpan.FromSeconds(3));

        Assert.Contains("NoMonitoredApps", cut.Markup);
    }

    [Fact]
    public void VisitorsView_Success_RendersDailyCount()
    {
        _handler.SetJsonResponse("api/appmonitoring/apps/5/visitors?days=30", new AppVisitorSeriesDto
        {
            Today = 3
        });

        var cut = Render<AppVisitorsView>(parameters => parameters.Add(component => component.AppId, 5));
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("UniqueVisitorsToday", cut.Markup);
            Assert.Contains(">3<", cut.Markup);
            Assert.DoesNotContain("LoadFailed", cut.Markup);
        });
    }

    [Fact]
    public void VisitorsView_HttpFailure_ShowsErrorAndRetry()
    {
        _handler.SetResponse("api/appmonitoring/apps/5/visitors?days=30", HttpStatusCode.ServiceUnavailable);

        var cut = Render<AppVisitorsView>(parameters => parameters.Add(component => component.AppId, 5));
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("LoadFailed", cut.Markup);
            Assert.Contains("Retry", cut.Markup);
            Assert.DoesNotContain("UniqueVisitorsToday", cut.Markup);
        });
    }

    [Fact]
    public void WebAnalyticsView_RendersPeriodCountsAndIngestionHealth()
    {
        _handler.SetJsonResponse("api/appmonitoring/apps/5/web-analytics?days=30", new AppWebAnalyticsSummaryDto
        {
            UniqueVisitorsToday = 3,
            UniqueVisitorsThisWeek = 8,
            UniqueVisitorsThisMonth = 12,
            SessionsThisMonth = 15,
            PageViewsThisMonth = 24,
            BrowserPerformanceSamplesLast30Days = 9,
            AverageBrowserNavigationDurationMs = 125,
            P95BrowserNavigationDurationMs = 240,
            BrowserErrorsLast30Days = 1,
            RejectedEvents = 2,
            EstimatedStorageBytes = 50,
            StorageBudgetBytes = 100,
            StorageUsagePercent = 50
        });

        var cut = Render<AppWebAnalyticsView>(parameters => parameters.Add(component => component.AppId, 5));
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("UniqueVisitorsThisWeek", cut.Markup);
            Assert.Contains(">8<", cut.Markup);
            Assert.Contains("AnalyticsIngestionHealth", cut.Markup);
            Assert.Contains("RejectedAnalyticsEvents", cut.Markup);
            Assert.Contains("BrowserPerformanceSamplesLast30Days", cut.Markup);
            Assert.Contains(">9<", cut.Markup);
            Assert.Contains("AverageBrowserNavigationDuration", cut.Markup);
            Assert.Contains(">125 ms<", cut.Markup);
            Assert.Contains("P95BrowserNavigationDuration", cut.Markup);
            Assert.Contains(">240 ms<", cut.Markup);
            Assert.Contains("BrowserErrorsLast30Days", cut.Markup);
        });
    }
}
