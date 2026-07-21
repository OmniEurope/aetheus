// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Dashboard;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;

namespace Aetheus.Front.Tests.Pages.Dashboard;

public class HomeDeepTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly BunitTestHelper.TestHandler _handler;

    public HomeDeepTests()
    {
        _handler = BunitTestHelper.RegisterServices(this, authenticated: true);
        _handler.SetJsonResponse(
            HttpMethod.Get,
            "api/appmonitoring/summary",
            new AppMonitoringSummaryDto());
    }

    private static DashboardOverviewDto MakeDashboard(int servers = 3, int online = 2) =>
        new()
        {
            TotalServers = servers,
            OnlineServers = online,
            OfflineServers = servers - online,
            PendingTasks = 1,
            RunningPipelines = 0,
            RecentRuns =
            [
                new PipelineRunDto
                {
                    Id = 1, PipelineId = 1, PipelineName = "CI", Status = PipelineStatus.Success,
                    StartedAt = DateTime.UtcNow.AddMinutes(-10)
                }
            ],
            Servers = [],
            Projects = []
        };

    [Fact]
    public void Renders_WithDashboardData()
    {
        _handler.SetJsonResponse("api/monitoring/dashboard", MakeDashboard());
        var cut = Render<Home>();
        cut.WaitForState(() => !cut.Markup.Contains("rz-progressbar-circular"), TimeSpan.FromSeconds(2));
        // OnInitializedAsync fetches the overview and stores it in _dashboard with the stub's counts.
        Assert.Contains(_handler.Requests, r => r.Url.Contains("api/monitoring/dashboard"));
        var dash = (DashboardOverviewDto?)typeof(Home).GetField("_dashboard", Priv)!.GetValue(cut.Instance);
        Assert.NotNull(dash);
        Assert.Equal(3, dash.TotalServers);
        Assert.Equal(2, dash.OnlineServers);
    }

    // Challenge (Moyen): the app-summary "Degraded" tile badge is rendered only when DegradedCount > 0
    // (`@if (_appSummary?.DegradedCount > 0)`), a conditional path that had no coverage.
    [Theory]
    [InlineData(2, true)]
    [InlineData(0, false)]
    public void DegradedBadge_RendersOnlyWhenDegradedCountPositive(int degraded, bool expectBadge)
    {
        _handler.SetJsonResponse("api/monitoring/dashboard", MakeDashboard());
        _handler.SetJsonResponse(HttpMethod.Get, "api/appmonitoring/summary",
            new AppMonitoringSummaryDto { UpCount = 5, DownCount = 1, DegradedCount = degraded, UnknownCount = 0 });

        var cut = Render<Home>();
        cut.WaitForAssertion(() =>
        {
            var summary = (AppMonitoringSummaryDto?)typeof(Home).GetField("_appSummary", Priv)!.GetValue(cut.Instance);
            Assert.NotNull(summary);
            Assert.Equal(degraded, summary.DegradedCount);
        }, TimeSpan.FromSeconds(2));

        // BadgeStyle.Warning is unique to the Degraded badge in the app-summary row (Up=Success, Down=Danger,
        // Unknown=Light), so its presence/absence tracks the conditional exactly.
        Assert.Equal(expectBadge, cut.Markup.Contains("rz-badge-warning", StringComparison.Ordinal));
    }

    [Fact]
    public void Renders_NullDashboard_OnHttpError()
    {
        _handler.SetResponse("api/monitoring/dashboard", System.Net.HttpStatusCode.InternalServerError);
        var cut = Render<Home>();
        // With a 500 error, Home catches HttpRequestException and _dashboard stays null.
        // Wait deterministically for the init fetch to have been attempted (no fixed sleep).
        cut.WaitForAssertion(
            () => Assert.Contains(_handler.Requests, r => r.Url.Contains("api/monitoring/dashboard")),
            TimeSpan.FromSeconds(2));
        var dash = (DashboardOverviewDto?)typeof(Home).GetField("_dashboard", Priv)!.GetValue(cut.Instance);
        // The 500 makes GetDashboardAsync throw HttpRequestException; Home swallows it and _dashboard stays null.
        Assert.Null(dash);
    }

    [Fact]
    public void Redirects_WhenNotAuthenticated()
    {
        var ctx2 = new BunitContext();
        BunitTestHelper.RegisterServices(ctx2, authenticated: false);
        var cut = ctx2.Render<Home>();
        var nav = ctx2.Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        Assert.Contains("login", nav.Uri);
    }

    [Fact]
    public void GetRunBadge_Success_ReturnsBadgeStyle()
    {
        var method = typeof(Home).GetMethod("GetRunBadge", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (BadgeStyle)method.Invoke(null, [PipelineStatus.Success])!;
        Assert.Equal(BadgeStyle.Success, result);
    }

    [Fact]
    public void GetRunBadge_Failed_ReturnsDanger()
    {
        var method = typeof(Home).GetMethod("GetRunBadge", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (BadgeStyle)method.Invoke(null, [PipelineStatus.Failed])!;
        Assert.Equal(BadgeStyle.Danger, result);
    }

    [Fact]
    public void Dashboard_HasStats_WhenLoaded()
    {
        _handler.SetJsonResponse("api/monitoring/dashboard", MakeDashboard(5, 3));
        var cut = Render<Home>();
        cut.WaitForState(() => !cut.Markup.Contains("rz-progressbar-circular"), TimeSpan.FromSeconds(2));
        var dash = (DashboardOverviewDto?)typeof(Home).GetField("_dashboard", Priv)!.GetValue(cut.Instance);
        Assert.NotNull(dash);
        Assert.Equal(5, dash.TotalServers);
        Assert.Equal(3, dash.OnlineServers);
    }

    [Fact]
    public void Dashboard_WithRecentRuns_Renders()
    {
        var overview = MakeDashboard() with
        {
            RecentRuns =
            [
                new PipelineRunDto { Id = 10, PipelineId = 2, PipelineName = "Deploy", Status = PipelineStatus.Running, StartedAt = DateTime.UtcNow }
            ]
        };
        _handler.SetJsonResponse("api/monitoring/dashboard", overview);
        var cut = Render<Home>();
        cut.WaitForState(() => !cut.Markup.Contains("rz-progressbar-circular"), TimeSpan.FromSeconds(2));
        // The single recent run from the stub ("Deploy", Running) lands in _dashboard.RecentRuns.
        var dash = (DashboardOverviewDto?)typeof(Home).GetField("_dashboard", Priv)!.GetValue(cut.Instance);
        Assert.NotNull(dash);
        var run = Assert.Single(dash.RecentRuns);
        Assert.Equal("Deploy", run.PipelineName);
        Assert.Equal(PipelineStatus.Running, run.Status);
    }
}
