// SPDX-License-Identifier: EUPL-1.2
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aetheus.Front.Tests.Services;

/// <summary>
/// Extended coverage for ServerDetailLoader: EnsureLoadedAsync, OnChanged, capability flags
/// driven by Service entries, switch-server teardown, and DisposeAsync paths.
/// </summary>
public class ServerDetailLoaderExtendedTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public ServerDetailLoaderExtendedTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    private ServerDetailLoader CreateLoader() => new(
        Services.GetRequiredService<ApiClient>(),
        Services.GetRequiredService<HubConnectionFactory>(),
        Services.GetRequiredService<NavigationManager>(),
        NullLogger<ServerDetailLoader>.Instance);

    private static void SetServer(ServerDetailLoader loader, ServerDetailDto? server) =>
        typeof(ServerDetailLoader).GetProperty(nameof(ServerDetailLoader.Server))!
            .SetValue(loader, server);

    private static void SetCurrentId(ServerDetailLoader loader, int? id) =>
        typeof(ServerDetailLoader).GetField("_currentId",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(loader, id);

    [Fact]
    public void ResolveSudoersCapabilities_DegradedLegacyHeartbeat_PreservesKnownValues()
    {
        var server = new ServerDetailDto
        {
            PackageManagementAvailable = true,
            MailSetupAvailable = true,
            DeploymentTargetAvailable = true
        };

        var result = ServerDetailLoader.ResolveSudoersCapabilities(server, new ServerHeartbeatDto());

        Assert.True(result.PackageManagement);
        Assert.True(result.MailSetup);
        Assert.True(result.DeploymentTarget);
    }

    [Fact]
    public void ResolveSudoersCapabilities_CompletedEmptyInventory_RevokesKnownValues()
    {
        var server = new ServerDetailDto
        {
            PackageManagementAvailable = true,
            MailSetupAvailable = true,
            DeploymentTargetAvailable = true
        };

        var result = ServerDetailLoader.ResolveSudoersCapabilities(server, new ServerHeartbeatDto
        {
            SudoersInventoryAvailable = true
        });

        Assert.False(result.PackageManagement);
        Assert.False(result.MailSetup);
        Assert.False(result.DeploymentTarget);
    }

    [Fact]
    public void ResolveSudoersCapabilities_PositiveLegacyInventory_RemainsAuthoritative()
    {
        var result = ServerDetailLoader.ResolveSudoersCapabilities(new ServerDetailDto(), new ServerHeartbeatDto
        {
            SudoersHashes = new Dictionary<string, string>
            {
                ["aetheus-deploy"] = new('A', 64)
            }
        });

        Assert.False(result.PackageManagement);
        Assert.False(result.MailSetup);
        Assert.True(result.DeploymentTarget);
    }

    // ── Capability flags via Services list ───────────────────────────────────

    [Fact]
    public void HasDocker_TrueWhenDockerServiceInstalled()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto
        {
            Id = 1,
            Services = [new ServiceInfoDto { Name = "docker", IsInstalled = true, IsRunning = true }]
        });
        Assert.True(sut.HasDocker);
    }

    [Fact]
    public void HasDocker_TrueWhenDockerHasImages()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto
        {
            Id = 1,
            Docker = new DockerDataDto
            {
                Containers = [],
                Images = [new DockerImageDto { ImageId = "i1", Repository = "nginx", Tag = "latest", Size = "100MB" }],
                ComposeStacks = []
            }
        });
        Assert.True(sut.HasDocker);
    }

    [Fact]
    public void HasDocker_TrueWhenDockerHasComposeStacks()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto
        {
            Id = 1,
            Docker = new DockerDataDto
            {
                Containers = [],
                Images = [],
                ComposeStacks = [new DockerComposeStackDto { Name = "app", Status = "running", RunningCount = 1, TotalCount = 1 }]
            }
        });
        Assert.True(sut.HasDocker);
    }

    [Fact]
    public void HasCron_TrueWhenCrondServiceInstalled()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto
        {
            Id = 1,
            Services = [new ServiceInfoDto { Name = "crond", IsInstalled = true, IsRunning = true }]
        });
        Assert.True(sut.HasCron);
    }

    [Fact]
    public void HasCron_TrueWhenCronServiceInstalled()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto
        {
            Id = 1,
            Services = [new ServiceInfoDto { Name = "cron", IsInstalled = true, IsRunning = true }]
        });
        Assert.True(sut.HasCron);
    }

    [Fact]
    public void HasMail_TrueWhenPostfixServiceInstalled()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto
        {
            Id = 1,
            Services = [new ServiceInfoDto { Name = "postfix", IsInstalled = true, IsRunning = true }]
        });
        Assert.True(sut.HasMail);
    }

    [Fact]
    public void HasMail_TrueWhenDovecotServiceInstalled()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto
        {
            Id = 1,
            Services = [new ServiceInfoDto { Name = "dovecot", IsInstalled = true, IsRunning = true }]
        });
        Assert.True(sut.HasMail);
    }

    [Fact]
    public void HasPortsentry_TrueWhenServiceInstalled()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto
        {
            Id = 1,
            Services = [new ServiceInfoDto { Name = "portsentry", IsInstalled = true, IsRunning = true }]
        });
        Assert.True(sut.HasPortsentry);
    }

    [Fact]
    public void HasRkhunter_TrueWhenServiceInstalled()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto
        {
            Id = 1,
            Services = [new ServiceInfoDto { Name = "rkhunter", IsInstalled = true, IsRunning = true }]
        });
        Assert.True(sut.HasRkhunter);
    }

    [Fact]
    public void HasCertbot_TrueWhenServiceInstalled()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto
        {
            Id = 1,
            Services = [new ServiceInfoDto { Name = "certbot", IsInstalled = true, IsRunning = true }]
        });
        Assert.True(sut.HasCertbot);
    }

    [Fact]
    public void HasApache2_TrueWhenApache2ServiceInstalled()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto
        {
            Id = 1,
            Services = [new ServiceInfoDto { Name = "apache2", IsInstalled = true, IsRunning = true }]
        });
        Assert.True(sut.HasApache);
    }

    // ── Service not installed = false ───────────────────────────────────────

    [Fact]
    public void HasDocker_FalseWhenServiceNotInstalled()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto
        {
            Id = 1,
            Services = [new ServiceInfoDto { Name = "docker", IsInstalled = false, IsRunning = false }]
        });
        // Not installed → false (Type=Normal, no containers/images/stacks)
        Assert.False(sut.HasDocker);
    }

    // ── OnChanged event ──────────────────────────────────────────────────────

    [Fact]
    public void NavigatingToServers_DoesNotClearServer()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto { Id = 1 });
        SetCurrentId(sut, 1);

        // Navigate within /servers/{id}/* - should NOT teardown
        Services.GetRequiredService<NavigationManager>().NavigateTo("/servers/1/overview");

        Assert.NotNull(sut.Server);
    }

    [Fact]
    public void NavigatingAwayFromServers_ClearsServer()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto { Id = 1 });
        SetCurrentId(sut, 1);

        Services.GetRequiredService<NavigationManager>().NavigateTo("/projects");

        Assert.Null(sut.Server);
    }

    [Fact]
    public async Task DisposeAsync_UnsubscribesFromNavigationEvents()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto { Id = 1 });
        SetCurrentId(sut, 1);

        await sut.DisposeAsync();

        // After dispose, navigation should not affect anything (no exception)
        Services.GetRequiredService<NavigationManager>().NavigateTo("/projects");
        Assert.Null(sut.Server);
    }

    // ── OnTaskCompleted event registration ──────────────────────────────────

    [Fact]
    public async Task OnTaskCompleted_CanSubscribeAndUnsubscribe()
    {
        var sut = CreateLoader();
        var called = false;
        Func<TaskCompletedNotification, Task> handler = _ =>
        {
            called = true;
            return Task.CompletedTask;
        };
        sut.OnTaskCompleted += handler;
        sut.OnTaskCompleted -= handler;
        var remaining = (Func<TaskCompletedNotification, Task>?)typeof(ServerDetailLoader)
            .GetField("OnTaskCompleted", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(sut);
        if (remaining is not null)
            await remaining(new TaskCompletedNotification { ServerId = 1 });
        Assert.False(called);
    }

    [Fact]
    public void OnChanged_CanSubscribeAndUnsubscribe()
    {
        var sut = CreateLoader();
        var count = 0;
        Action handler = () => count++;
        sut.OnChanged += handler;
        sut.OnChanged -= handler;
        var remaining = (Action?)typeof(ServerDetailLoader)
            .GetField("OnChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(sut);
        remaining?.Invoke();
        Assert.Equal(0, count);
    }

    // ── InitialLoadCompleted / MetricsReceived defaults ─────────────────────

    [Fact]
    public void InitialState_PropertiesHaveCorrectDefaults()
    {
        var sut = CreateLoader();
        Assert.Null(sut.Server);
        Assert.Null(sut.LastUpdated);
        Assert.False(sut.MetricsReceived);
        Assert.False(sut.InitialLoadCompleted);
    }
}
