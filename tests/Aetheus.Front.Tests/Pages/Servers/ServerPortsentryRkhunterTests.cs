// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Servers.ServerDetailSections;
using Aetheus.Shared.DTOs;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Servers;

public class ServerPortsentryRkhunterTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public ServerPortsentryRkhunterTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    // ──── Portsentry ────

    [Fact]
    public void Portsentry_NotInstalled_RendersNotInstalledState()
    {
        _handler.SetJsonResponse("api/servers/7/portsentry/whitelist", new PaginatedResult<PortsentryWhitelistIpDto>());

        var ps = new PortsentryDataDto { IsInstalled = false };
        var cut = Render<ServerPortsentrySection>(p => p
            .Add(x => x.ServerId, 7)
            .Add(x => x.Ps, ps));

        Assert.Contains("PortSentry", cut.Markup);
        Assert.Contains("NotInstalled", cut.Markup);
    }

    [Fact]
    public void Portsentry_Installed_RendersStatusAndBlockedCount()
    {
        _handler.SetJsonResponse("api/servers/7/portsentry/whitelist",
            new PaginatedResult<PortsentryWhitelistIpDto>
            {
                Items = [new() { Id = 1, IpAddress = "192.168.1.10", Description = "Dev box" }],
                TotalCount = 1
            });
        _handler.SetJsonResponse("api/servers/7/portsentry/blocked", new PaginatedResult<PortsentryBlockedIpDto>());

        var ps = new PortsentryDataDto
        {
            IsInstalled = true,
            IsRunning = true,
            Version = "1.2",
            Mode = "atcp",
            BlockedCount = 3
        };
        var cut = Render<ServerPortsentrySection>(p => p
            .Add(x => x.ServerId, 7)
            .Add(x => x.Ps, ps));

        Assert.Contains("PortSentry", cut.Markup);
        Assert.Contains("Installed", cut.Markup);
        Assert.Contains("v1.2", cut.Markup);
    }

    [Fact]
    public void Portsentry_BlockedCountBadgeStyle_ForZeroShowsSuccess()
    {
        _handler.SetJsonResponse("api/servers/7/portsentry/whitelist", new PaginatedResult<PortsentryWhitelistIpDto>());
        _handler.SetJsonResponse("api/servers/7/portsentry/blocked", new PaginatedResult<PortsentryBlockedIpDto>());
        var ps = new PortsentryDataDto { IsInstalled = true, IsRunning = true, BlockedCount = 0 };
        var cut = Render<ServerPortsentrySection>(p => p
            .Add(x => x.ServerId, 7)
            .Add(x => x.Ps, ps));
        Assert.Contains("BlockedIps", cut.Markup);
    }

    [Fact]
    public void Portsentry_ManyBlocked_RendersDangerBadge()
    {
        _handler.SetJsonResponse("api/servers/7/portsentry/whitelist", new PaginatedResult<PortsentryWhitelistIpDto>());
        _handler.SetJsonResponse("api/servers/7/portsentry/blocked", new PaginatedResult<PortsentryBlockedIpDto>());
        var ps = new PortsentryDataDto { IsInstalled = true, IsRunning = true, BlockedCount = 25 };
        var cut = Render<ServerPortsentrySection>(p => p
            .Add(x => x.ServerId, 7)
            .Add(x => x.Ps, ps));
        Assert.Contains("25", cut.Markup);
    }

    // ──── Rkhunter ────

    [Fact]
    public void Rkhunter_NotInstalled_RendersNotInstalledState()
    {
        _handler.SetJsonResponse("api/servers/8/rkhunter/warnings", new List<RkhunterWarningDto>());
        var rk = new RkhunterDataDto { IsInstalled = false };
        var cut = Render<ServerRkhunterSection>(p => p
            .Add(x => x.ServerId, 8)
            .Add(x => x.Rk, rk));

        Assert.Contains("RKHunter", cut.Markup);
    }

    [Fact]
    public void Rkhunter_Installed_RendersVersion()
    {
        _handler.SetJsonResponse("api/servers/8/rkhunter/warnings", new List<RkhunterWarningDto>());
        var rk = new RkhunterDataDto
        {
            IsInstalled = true,
            Version = "1.4.6",
            LastScanTime = DateTime.UtcNow.AddDays(-1),
            WarningCount = 2
        };
        var cut = Render<ServerRkhunterSection>(p => p
            .Add(x => x.ServerId, 8)
            .Add(x => x.Rk, rk));

        Assert.Contains("RKHunter", cut.Markup);
        Assert.Contains("1.4.6", cut.Markup);
    }

    [Fact]
    public void Rkhunter_WithWarnings_RendersWarningCount()
    {
        _handler.SetJsonResponse("api/servers/8/rkhunter/warnings",
            new List<RkhunterWarningDto>
            {
                new() { Id = 1, Category = "rootkit", Detail = "suspicious file", Severity = "warning", FoundAt = DateTime.UtcNow }
            });
        var rk = new RkhunterDataDto
        {
            IsInstalled = true,
            Version = "1.4.6",
            WarningCount = 3
        };
        var cut = Render<ServerRkhunterSection>(p => p
            .Add(x => x.ServerId, 8)
            .Add(x => x.Rk, rk));

        Assert.Contains("3", cut.Markup);
    }
}
