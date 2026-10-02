// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Components.Servers.ServerDetailSections;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Servers;

public class ServerModulesAppsSectionTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public ServerModulesAppsSectionTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    // ──── Modules ────

    [Fact]
    public void ModulesSection_EmptyList_Renders()
    {
        _handler.SetJsonResponse("api/servers/30/modules", new List<ServerModuleDto>());
        var cut = Render<ServerModulesSection>(p => p.Add(x => x.ServerId, 30));
        // After the fetch resolves, the empty-grid text renders; the title heading is gone (page header shows it).
        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain(">Modules<", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("NoRecords", cut.Markup);
        });
    }

    [Fact]
    public void ModulesSection_WithModules_RendersRows()
    {
        _handler.SetJsonResponse("api/servers/30/modules", new List<ServerModuleDto>
        {
            new() { Id = 1, Name = "web", Type = ServerModuleType.Docker, Version = "1.0.0", Status = ServerModuleStatus.Active },
            new() { Id = 2, Name = "db", Type = ServerModuleType.PostgreSQL, Version = "15", Status = ServerModuleStatus.Inactive }
        });
        var cut = Render<ServerModulesSection>(p => p.Add(x => x.ServerId, 30));
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("web", cut.Markup);
            Assert.Contains("db", cut.Markup);
        });
    }

    [Theory]
    [InlineData(ServerModuleStatus.Active)]
    [InlineData(ServerModuleStatus.Inactive)]
    [InlineData(ServerModuleStatus.Error)]
    [InlineData(ServerModuleStatus.Unknown)]
    public void GetModuleStatusBadge_AllStatuses_ReturnBadge(ServerModuleStatus status)
    {
        var method = typeof(ServerModulesSection).GetMethod("GetModuleStatusBadge",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var badge = method.Invoke(null, [status]);
        Assert.NotNull(badge);
    }

    // ──── Apps ────

    [Fact]
    public void AppsSection_EmptyList_Renders()
    {
        _handler.SetPaginatedJsonResponse("api/servers/31/apps", new List<ServerAppDto>());
        var cut = Render<ServerAppsSection>(p => p.Add(x => x.ServerId, 31));
        // After the fetch resolves, the empty-grid text renders; the title heading is gone (page header shows it).
        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain(">Applications<", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("NoRecords", cut.Markup);
        });
    }

    [Fact]
    public void AppsSection_WithApps_RendersList()
    {
        _handler.SetPaginatedJsonResponse("api/servers/31/apps", new List<ServerAppDto>
        {
            new() { Id = 1, Name = "nginx", Version = "1.24", Path = "/usr/sbin/nginx" },
            new() { Id = 2, Name = "redis", Version = "7.0", Path = "/usr/bin/redis-server" }
        });
        var cut = Render<ServerAppsSection>(p => p.Add(x => x.ServerId, 31));
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("nginx", cut.Markup);
            Assert.Contains("redis", cut.Markup);
        });
    }
}
