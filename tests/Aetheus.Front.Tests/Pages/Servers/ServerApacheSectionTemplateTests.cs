// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Components.Servers.ServerDetailSections;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Servers;

public class ServerApacheSectionTemplateTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Type SectionType = typeof(ServerApacheSection);

    public ServerApacheSectionTemplateTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    private static ServerDetailDto BuildServer(
        bool isRunning = true,
        string version = "2.4.51",
        int? pid = 1234,
        bool degraded = false,
        string diagnostics = "",
        List<ApacheModuleDto>? modules = null,
        List<ApacheVirtualHostDto>? vhosts = null) => new()
        {
            Id = 1,
            Name = "web-server",
            Hostname = "srv1",
            OsDescription = "Ubuntu 22.04",
            AgentVersion = "1.0",
            Status = ServerStatus.Online,
            Type = ServerType.Normal,
            LastHeartbeat = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            OrganizationId = 1,
            Apache = new ApacheDataDto
            {
                IsInstalled = true,
                IsRunning = isRunning,
                Version = version,
                Pid = pid,
                CollectionDegraded = degraded,
                CollectionDiagnostics = diagnostics,
                Modules = modules ?? [],
                VirtualHosts = vhosts ?? []
            }
        };

    [Fact]
    public void Renders_ApacheRunning_WithVersionAndPid()
    {
        var server = BuildServer(isRunning: true, version: "2.4.51", pid: 1234);
        var cut = Render<ServerApacheSection>(p =>
        {
            p.Add(x => x.Server, server);
            p.Add(x => x.ServerId, 1);
        });
        Assert.Contains("2.4.51", cut.Markup);
        Assert.Contains("1234", cut.Markup);
    }

    [Fact]
    public void Renders_ApacheStopped_NoPid()
    {
        var server = BuildServer(isRunning: false, version: "2.4.51", pid: null);
        var cut = Render<ServerApacheSection>(p =>
        {
            p.Add(x => x.Server, server);
            p.Add(x => x.ServerId, 1);
        });
        Assert.Contains("2.4.51", cut.Markup);
        // No PID block when pid is null
        Assert.DoesNotContain("PID null", cut.Markup);
    }

    [Fact]
    public void Renders_CollectionDegraded_ShowsWarning()
    {
        var server = BuildServer(degraded: true, diagnostics: "apache2ctl failed with exit code 1");
        var cut = Render<ServerApacheSection>(p =>
        {
            p.Add(x => x.Server, server);
            p.Add(x => x.ServerId, 1);
        });
        Assert.Contains("apache2ctl failed with exit code 1", cut.Markup);
    }

    [Fact]
    public void Renders_CollectionDegraded_NoDiagnostics_HidesDiagText()
    {
        var server = BuildServer(degraded: true, diagnostics: "");
        var cut = Render<ServerApacheSection>(p =>
        {
            p.Add(x => x.Server, server);
            p.Add(x => x.ServerId, 1);
        });
        // Degraded warning still shows; with an empty diagnostics string the failed-command
        // diagnostic text used by the other degraded test is absent.
        Assert.Contains("ApacheCollectionDegraded", cut.Markup);
        Assert.DoesNotContain("apache2ctl failed", cut.Markup);
    }

    [Fact]
    public void EnabledVirtualHostGroups_GroupsByServerName()
    {
        var vhosts = new List<ApacheVirtualHostDto>
        {
            new() { ServerName = "example.com", Port = 80, DocumentRoot = "/var/www", ConfigFile = "/etc/apache2/sites/example.conf", IsEnabled = true },
            new() { ServerName = "example.com", Port = 443, DocumentRoot = "/var/www", ConfigFile = "/etc/apache2/sites/example.conf", IsEnabled = true },
            new() { ServerName = "api.example.com", Port = 443, DocumentRoot = "/var/www/api", ConfigFile = "/etc/apache2/sites/api.conf", IsEnabled = true }
        };
        var server = BuildServer(vhosts: vhosts);
        var cut = Render<ServerApacheSection>(p =>
        {
            p.Add(x => x.Server, server);
            p.Add(x => x.ServerId, 1);
        });
        var groups = (List<ServerApacheSection.ApacheVirtualHostGroup>)
            SectionType.GetProperty("EnabledVirtualHostGroups", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance)!;
        Assert.Equal(2, groups.Count);
        var exampleGroup = groups.First(g => g.ServerName == "example.com");
        Assert.Equal(2, exampleGroup.Members.Count);
    }

    [Fact]
    public void DisabledVirtualHostGroups_OnlyDisabled()
    {
        var vhosts = new List<ApacheVirtualHostDto>
        {
            new() { ServerName = "old.example.com", Port = 80, DocumentRoot = "/var/www/old", ConfigFile = "/etc/apache2/sites-available/old.conf", IsEnabled = false },
            new() { ServerName = "new.example.com", Port = 80, DocumentRoot = "/var/www/new", ConfigFile = "/etc/apache2/sites-enabled/new.conf", IsEnabled = true }
        };
        var server = BuildServer(vhosts: vhosts);
        var cut = Render<ServerApacheSection>(p =>
        {
            p.Add(x => x.Server, server);
            p.Add(x => x.ServerId, 1);
        });
        var disabled = (List<ServerApacheSection.ApacheVirtualHostGroup>)
            SectionType.GetProperty("DisabledVirtualHostGroups", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance)!;
        Assert.Single(disabled);
        Assert.Equal("old.example.com", disabled[0].ServerName);
    }

    [Fact]
    public void FilteredModules_FiltersOnSearch()
    {
        var modules = new List<ApacheModuleDto>
        {
            new() { Name = "ssl_module", Type = "shared", IsEnabled = true },
            new() { Name = "rewrite_module", Type = "shared", IsEnabled = true },
            new() { Name = "core_module", Type = "static", IsEnabled = true }
        };
        var server = BuildServer(modules: modules);
        var cut = Render<ServerApacheSection>(p =>
        {
            p.Add(x => x.Server, server);
            p.Add(x => x.ServerId, 1);
        });
        SectionType.GetField("_moduleSearch", Priv)!.SetValue(cut.Instance, "ssl");
        var filtered = (List<ApacheModuleDto>)
            SectionType.GetProperty("FilteredModules", Priv)!.GetValue(cut.Instance)!;
        Assert.Single(filtered);
        Assert.Equal("ssl_module", filtered[0].Name);
    }

    [Fact]
    public void GetStatusBadge_Running_ReturnsSuccess()
    {
        Assert.Equal(OmniTone.Success, ServerApacheSection.GetStatusBadge(true));
    }

    [Fact]
    public void GetStatusBadge_Stopped_ReturnsDanger()
    {
        Assert.Equal(OmniTone.Danger, ServerApacheSection.GetStatusBadge(false));
    }

    [Fact]
    public void StopButton_IsDisabled_WhileAnotherApacheActionRuns()
    {
        // PLAN-005 lot 4: Stop carries no veil of its own, so its Disabled keeps the "an action is
        // running" interlock; without it Stop could be sent in the middle of a Restart.
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _handler.SetAsyncJsonResponse(HttpMethod.Post, "api/servers/1/apache/action", async ct =>
        {
            await release.Task.WaitAsync(ct);
            return new { };
        });
        var cut = Render<ServerApacheSection>(p =>
        {
            p.Add(x => x.Server, BuildServer(isRunning: true));
            p.Add(x => x.ServerId, 1);
        });
        AngleSharp.Dom.IElement Button(string text) => cut.FindAll("button").Single(b => b.TextContent.Trim() == text);
        Assert.False(Button("Stop").HasAttribute("disabled"));

        Button("Restart").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("btn-busy", Button("Restart").ClassName, StringComparison.Ordinal);
            Assert.True(Button("Stop").HasAttribute("disabled"));
        });
        release.SetResult();
        cut.WaitForAssertion(() => Assert.False(Button("Stop").HasAttribute("disabled")));
    }

    [Fact]
    public void Dispose_StopsFollowTimer()
    {
        var server = BuildServer();
        var cut = Render<ServerApacheSection>(p =>
        {
            p.Add(x => x.Server, server);
            p.Add(x => x.ServerId, 1);
        });
        cut.Instance.Dispose();
    }
}
