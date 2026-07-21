// SPDX-License-Identifier: EUPL-1.2
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
    }

    [Fact]
    public void RendersAppPicker_AndTelemetrySubTabs()
    {
        SeedTelemetry();
        var cut = Render<AppTelemetryPanel>(p => p.Add(c => c.ProjectId, 1));
        cut.WaitForState(() => cut.Markup.Contains("toto-qa"), TimeSpan.FromSeconds(3));

        Assert.Contains("toto-qa", cut.Markup);        // app picker populated
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
}
