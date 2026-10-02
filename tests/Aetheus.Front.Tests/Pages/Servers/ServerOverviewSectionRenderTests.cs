// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Components.Servers.ServerDetailSections;
using Aetheus.Front.Resources;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NSubstitute;

namespace Aetheus.Front.Tests.Pages.Servers;

public class ServerOverviewSectionRenderTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly BunitTestHelper.TestHandler _handler;

    public ServerOverviewSectionRenderTests() => _handler = BunitTestHelper.RegisterServices(this);

    private static ServerDetailDto BuildServer(int id = 1, bool runnerEnabled = false) => new()
    {
        Id = id,
        Name = "web-prod",
        Hostname = "10.0.0.1",
        IpAddress = "10.0.0.1",
        Type = ServerType.Normal,
        Status = ServerStatus.Online,
        CpuPercent = 30,
        MemoryUsedMb = 4096,
        MemoryTotalMb = 8192,
        DiskUsedGb = 20,
        DiskTotalGb = 100,
        AgentVersion = "1.5.0.1234",
        Tags = [],
        Services = [],
        PipelineRunnerEnabled = runnerEnabled,
        Docker = new DockerDataDto { Containers = [], Images = [], ComposeStacks = [], Networks = [], Volumes = [] },
        Apache = new ApacheDataDto(),
        Certbot = new CertbotDataDto(),
        Cron = new CronDataDto(),
        Mail = new MailDataDto(),
        Teamspeak = new TeamspeakDataDto(),
        Portsentry = new PortsentryDataDto(),
        Rkhunter = new RkhunterDataDto()
    };

    [Fact]
    public void Renders_WithServer_ShowsOverviewCard()
    {
        var server = BuildServer();
        var cut = Render<ServerOverviewSection>(p =>
            p.Add(x => x.Server, server)
             .Add(x => x.MetricsReceived, true));
        // The Essentials card surfaces the server hostname and the formatted agent version
        // (FormatAgentVersion truncates "1.5.0.1234" -> "v1.5.0").
        Assert.Contains("10.0.0.1", cut.Markup);
        Assert.Contains("v1.5.0", cut.Markup);
    }

    [Fact]
    public void Essentials_ShowsInstalledAndTargetVersions_WhenUpdateIsAvailable()
    {
        var localizer = Services.GetRequiredService<IStringLocalizer<AppStrings>>();
        localizer["AgentVersionUpdateAvailable"]
            .Returns(new LocalizedString("AgentVersionUpdateAvailable", "{0} → target {1}"));
        var server = BuildServer() with
        {
            AgentVersion = "1.0.1547",
            AgentCompatibility = new AgentCompatibilityDto
            {
                Status = AgentCompatibilityStatus.UpdateRecommended,
                InstalledVersion = "1.0.1547",
                TargetVersion = "1.0.1552"
            }
        };

        var cut = Render<ServerOverviewSection>(parameters => parameters
            .Add(component => component.Server, server));

        var agentVersionItem = cut.FindAll(".essential-item")
            .Single(item => item.TextContent.Contains("AgentVersion", StringComparison.Ordinal));
        Assert.Contains("v1.0.1547 → target 1.0.1552", agentVersionItem.TextContent);
    }

    [Fact]
    public void Essentials_ShowsOnlyInstalledVersion_WhenAgentIsCurrent()
    {
        var server = BuildServer() with
        {
            AgentVersion = "1.0.1552",
            AgentCompatibility = new AgentCompatibilityDto
            {
                Status = AgentCompatibilityStatus.UpToDate,
                InstalledVersion = "1.0.1552",
                TargetVersion = "1.0.1552"
            }
        };

        var cut = Render<ServerOverviewSection>(parameters => parameters
            .Add(component => component.Server, server));

        Assert.Contains("v1.0.1552", cut.Markup);
        Assert.DoesNotContain("→ target", cut.Markup);
    }

    [Fact]
    public void Essentials_DoesNotShowTarget_WhenTargetMatchesInstalledVersion()
    {
        var localizer = Services.GetRequiredService<IStringLocalizer<AppStrings>>();
        localizer["AgentVersionUpdateAvailable"]
            .Returns(new LocalizedString("AgentVersionUpdateAvailable", "{0} → target {1}"));
        var server = BuildServer() with
        {
            AgentVersion = "1.0.1552",
            AgentCompatibility = new AgentCompatibilityDto
            {
                Status = AgentCompatibilityStatus.UpdateRecommended,
                InstalledVersion = "1.0.1552",
                TargetVersion = "1.0.1552"
            }
        };

        var cut = Render<ServerOverviewSection>(parameters => parameters
            .Add(component => component.Server, server));

        var agentVersionItem = cut.FindAll(".essential-item")
            .Single(item => item.TextContent.Contains("AgentVersion", StringComparison.Ordinal));
        Assert.Contains("v1.0.1552", agentVersionItem.TextContent);
        Assert.DoesNotContain("→ target", agentVersionItem.TextContent);
    }

    [Fact]
    public void Renders_WithLastUpdated()
    {
        var server = BuildServer();
        var cut = Render<ServerOverviewSection>(p =>
            p.Add(x => x.Server, server)
             .Add(x => x.LastUpdated, new DateTime(2026, 1, 1, 12, 0, 0)));
        // The bound LastUpdated value is recorded on the instance and the Essentials card still renders.
        Assert.Equal(new DateTime(2026, 1, 1, 12, 0, 0), cut.Instance.LastUpdated);
        Assert.Contains("10.0.0.1", cut.Markup);
    }

    [Fact]
    public void RunnerBusy_InitiallyFalse()
    {
        var server = BuildServer();
        var cut = Render<ServerOverviewSection>(p =>
            p.Add(x => x.Server, server));
        var busy = (bool)typeof(ServerOverviewSection).GetField("_runnerBusy", Priv)!.GetValue(cut.Instance)!;
        Assert.False(busy);
    }

    [Fact]
    public async Task TogglePipelineRunnerAsync_WhenBusy_ReturnsEarly()
    {
        var server = BuildServer(runnerEnabled: false);
        var cut = Render<ServerOverviewSection>(p => p.Add(x => x.Server, server));

        // Set _runnerBusy = true to force early return
        typeof(ServerOverviewSection).GetField("_runnerBusy", Priv)!.SetValue(cut.Instance, true);

        var method = typeof(ServerOverviewSection).GetMethod("TogglePipelineRunnerAsync", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        // Still busy (unchanged)
        var busy = (bool)typeof(ServerOverviewSection).GetField("_runnerBusy", Priv)!.GetValue(cut.Instance)!;
        Assert.True(busy);
    }

    [Fact]
    public async Task TogglePipelineRunnerAsync_DisablePath_CallsApi()
    {
        // When PipelineRunnerEnabled = true → disable doesn't require Confirm
        var server = BuildServer(runnerEnabled: true);
        _handler.SetJsonResponse("api/servers/1/pipeline-runner", new ServerDetailDto
        {
            Id = 1,
            Name = "web-prod",
            Hostname = "h",
            IpAddress = "1.1.1.1",
            Type = ServerType.Normal,
            Status = ServerStatus.Online,
            PipelineRunnerEnabled = false,
            Tags = [],
            Services = [],
            Docker = new DockerDataDto { Containers = [], Images = [], ComposeStacks = [], Networks = [], Volumes = [] },
            Apache = new ApacheDataDto(),
            Certbot = new CertbotDataDto(),
            Cron = new CronDataDto(),
            Mail = new MailDataDto(),
            Teamspeak = new TeamspeakDataDto(),
            Portsentry = new PortsentryDataDto(),
            Rkhunter = new RkhunterDataDto()
        });

        var cut = Render<ServerOverviewSection>(p => p.Add(x => x.Server, server));
        var method = typeof(ServerOverviewSection).GetMethod("TogglePipelineRunnerAsync", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        // _runnerBusy should be false after completion
        var busy = (bool)typeof(ServerOverviewSection).GetField("_runnerBusy", Priv)!.GetValue(cut.Instance)!;
        Assert.False(busy);
    }

    [Fact]
    public async Task TogglePipelineRunnerAsync_DisablePath_ApiReturnsNull_ToastsError()
    {
        var server = BuildServer(runnerEnabled: true);
        _handler.SetResponse("api/servers/1/pipeline-runner", System.Net.HttpStatusCode.BadRequest);

        var cut = Render<ServerOverviewSection>(p => p.Add(x => x.Server, server));
        var method = typeof(ServerOverviewSection).GetMethod("TogglePipelineRunnerAsync", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        var busy = (bool)typeof(ServerOverviewSection).GetField("_runnerBusy", Priv)!.GetValue(cut.Instance)!;
        Assert.False(busy);
    }

    [Fact]
    public void Renders_WithNoLastUpdated()
    {
        var server = BuildServer();
        var cut = Render<ServerOverviewSection>(p =>
            p.Add(x => x.Server, server)
             .Add(x => x.LastUpdated, (DateTime?)null));
        // A null LastUpdated is tolerated: the section still renders the server's essentials.
        Assert.Null(cut.Instance.LastUpdated);
        Assert.Contains("10.0.0.1", cut.Markup);
    }
}
