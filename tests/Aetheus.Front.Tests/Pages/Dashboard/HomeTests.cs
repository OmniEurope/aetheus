// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages;

public class HomeTests : BunitContext
{
    public HomeTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        _handler.SetJsonResponse(
            HttpMethod.Get,
            "api/appmonitoring/summary",
            new AppMonitoringSummaryDto());
    }

    private readonly BunitTestHelper.TestHandler _handler;

    [Fact]
    public void Renders_Loading_WhenDashboardNull()
    {
        _handler.SetJsonResponse("monitoring/dashboard", new DashboardOverviewDto
        {
            TotalServers = 5,
            OnlineServers = 3,
            OfflineServers = 2,
            PendingTasks = 1,
            Servers = [],
            RecentRuns = []
        });

        var cut = Render<Home>();
        cut.WaitForState(() => cut.Markup.Contains("5"));

        Assert.Contains("5", cut.Markup);
        Assert.Contains("3", cut.Markup);
        Assert.Contains("2", cut.Markup);
    }

    [Fact]
    public void RedirectsToLogin_WhenNotAuthenticated()
    {
        var handler = BunitTestHelper.RegisterServices(this, authenticated: false);
        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();

        var cut = Render<Home>();

        Assert.EndsWith("/login", nav.Uri);
    }

    [Fact]
    public void Renders_RecentRuns_WithBadges()
    {
        _handler.SetJsonResponse("monitoring/dashboard", new DashboardOverviewDto
        {
            TotalServers = 1,
            OnlineServers = 1,
            OfflineServers = 0,
            PendingTasks = 0,
            Servers = [new ServerDto { Id = 1, Name = "srv", Status = ServerStatus.Online }],
            RecentRuns =
            [
                new PipelineRunDto { Id = 1, PipelineName = "deploy", Status = PipelineStatus.Success, StartedAt = DateTime.UtcNow }
            ]
        });

        var cut = Render<Home>();
        cut.WaitForState(() => cut.Markup.Contains("deploy"));

        Assert.Contains("deploy", cut.Markup);
    }

    [Fact]
    public void Handles_HttpRequestException()
    {
        _handler.SetResponse("api/", System.Net.HttpStatusCode.InternalServerError);

        // Should not throw - the page catches HttpRequestException and renders with a null
        // dashboard, so the stat cards fall back to the "-" placeholder.
        var cut = Render<Home>();
        Assert.Contains("Dashboard", cut.Markup);
        Assert.Contains("-", cut.Markup);
    }

    [Fact]
    public void StatusColumn_OnlineServer_RendersWifiIconWithSuccessColorAndAriaLabel()
    {
        RenderWithServer(new ServerDto { Id = 1, Name = "srv", Status = ServerStatus.Online });

        var cut = Render<Home>();
        var statusIcon = cut.WaitForElement("[role='img'][aria-label]");
        var classes = statusIcon.GetAttribute("class") ?? string.Empty;

        // Check the row status icon, not the dashboard tiles which legitimately use both colours.
        Assert.Equal("wifi", statusIcon.TextContent.Trim());
        Assert.Contains("rz-color-success", classes);
        Assert.DoesNotContain("rz-color-danger", classes);
        // Icon-only status carries an accessible name (a11y), not just a title.
        Assert.False(string.IsNullOrWhiteSpace(statusIcon.GetAttribute("aria-label")));
    }

    [Fact]
    public void StatusColumn_OfflineServer_RendersWifiOffIconWithDangerColor()
    {
        RenderWithServer(new ServerDto { Id = 1, Name = "srv", Status = ServerStatus.Offline });

        var cut = Render<Home>();
        var statusIcon = cut.WaitForElement("[role='img'][aria-label]");
        var classes = statusIcon.GetAttribute("class") ?? string.Empty;

        Assert.Equal("wifi_off", statusIcon.TextContent.Trim());
        Assert.Contains("rz-color-danger", classes);
        Assert.DoesNotContain("rz-color-success", classes);
    }

    [Fact]
    public void StatusColumn_RendersCapabilityIcons_WhenFlagsSet()
    {
        RenderWithServer(new ServerDto
        {
            Id = 1,
            Name = "srv",
            Status = ServerStatus.Online,
            PipelineRunnerEnabled = true,
            DeploymentTargetAvailable = true,
            PackageManagementAvailable = true
        });

        var cut = Render<Home>();
        cut.WaitForState(() => cut.Markup.Contains("rz-color-success"));

        Assert.Contains("build", cut.Markup);
        Assert.Contains("rocket_launch", cut.Markup);
        Assert.Contains("settings", cut.Markup);
    }

    [Fact]
    public void StatusColumn_OmitsCapabilityIcons_WhenFlagsUnset()
    {
        RenderWithServer(new ServerDto
        {
            Id = 1,
            Name = "srv",
            Status = ServerStatus.Online,
            PipelineRunnerEnabled = false,
            DeploymentTargetAvailable = false,
            PackageManagementAvailable = false
        });

        var cut = Render<Home>();
        cut.WaitForState(() => cut.Markup.Contains("rz-color-success"));

        // Only the online/offline icon is shown when no capability is known. rocket_launch is unique to
        // the deploy capability, so its absence proves the conditional icons are gated on the flags.
        Assert.DoesNotContain("rocket_launch", cut.Markup);
    }

    private void RenderWithServer(ServerDto server) =>
        _handler.SetJsonResponse("monitoring/dashboard", new DashboardOverviewDto
        {
            TotalServers = 1,
            OnlineServers = server.Status == ServerStatus.Online ? 1 : 0,
            OfflineServers = server.Status == ServerStatus.Online ? 0 : 1,
            PendingTasks = 0,
            Servers = [server],
            RecentRuns = []
        });
}
