// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aetheus.Front.Tests.Services;

public class ServerDetailLoaderTests : BunitContext
{
    public ServerDetailLoaderTests() => BunitTestHelper.RegisterServices(this);

    private ServerDetailLoader CreateLoader() => new(
        Services.GetRequiredService<ApiClient>(),
        Services.GetRequiredService<HubConnectionFactory>(),
        Services.GetRequiredService<NavigationManager>(),
        NullLogger<ServerDetailLoader>.Instance);

    private static void SetServer(ServerDetailLoader loader, ServerDetailDto server) =>
        typeof(ServerDetailLoader).GetProperty(nameof(ServerDetailLoader.Server))!.SetValue(loader, server);

    [Fact]
    public void InitialState_IsEmpty()
    {
        var sut = CreateLoader();
        Assert.Null(sut.Server);
        Assert.False(sut.InitialLoadCompleted);
        Assert.False(sut.MetricsReceived);
    }

    [Fact]
    public void Capabilities_AllFalse_WhenBareServer()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto { Id = 1 }); // Type=Normal, nothing installed
        Assert.False(sut.HasDocker);
        Assert.False(sut.HasApache);
        Assert.False(sut.HasCertbot);
        Assert.False(sut.HasCron);
        Assert.False(sut.HasMail);
        Assert.False(sut.HasTeamspeak);
        Assert.False(sut.HasPortsentry);
        Assert.False(sut.HasRkhunter);
    }

    [Fact]
    public void HasDocker_TrueWhenTypeDocker()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto { Id = 1, Type = ServerType.Docker });
        Assert.True(sut.HasDocker);
    }

    [Fact]
    public void HasDocker_TrueWhenDockerServiceInstalled()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto { Id = 1, Services = [new ServiceInfoDto { Name = "docker" }] });
        Assert.True(sut.HasDocker);
    }

    [Fact]
    public void HasApache_TrueWhenInstalled()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto { Id = 1, Apache = new ApacheDataDto { IsInstalled = true } });
        Assert.True(sut.HasApache);
    }

    [Fact]
    public void HasApache_TrueWhenApache2ServiceInstalled()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto { Id = 1, Services = [new ServiceInfoDto { Name = "apache2" }] });
        Assert.True(sut.HasApache);
    }

    [Fact]
    public void HasCertbot_TrueWhenInstalled()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto { Id = 1, Certbot = new CertbotDataDto { IsInstalled = true } });
        Assert.True(sut.HasCertbot);
    }

    [Fact]
    public void HasCron_TrueWhenInstalled()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto { Id = 1, Cron = new CronDataDto { IsInstalled = true } });
        Assert.True(sut.HasCron);
    }

    [Fact]
    public void HasMail_TrueWhenInstalled()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto { Id = 1, Mail = new MailDataDto { IsInstalled = true } });
        Assert.True(sut.HasMail);
    }

    [Fact]
    public void HasTeamspeak_TrueWhenInstalled()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto { Id = 1, Teamspeak = new TeamspeakDataDto { IsInstalled = true } });
        Assert.True(sut.HasTeamspeak);
    }

    [Fact]
    public void HasPortsentry_TrueWhenInstalled()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto { Id = 1, Portsentry = new PortsentryDataDto { IsInstalled = true } });
        Assert.True(sut.HasPortsentry);
    }

    [Fact]
    public void HasRkhunter_TrueWhenInstalled()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto { Id = 1, Rkhunter = new RkhunterDataDto { IsInstalled = true } });
        Assert.True(sut.HasRkhunter);
    }

    [Fact]
    public void NavigatingAwayFromServers_ClearsServer()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto { Id = 1 });

        Services.GetRequiredService<NavigationManager>().NavigateTo("/projects");

        Assert.Null(sut.Server);
    }

    [Fact]
    public void AppendMetric_MapsStorageAvailabilityAndPurgesOlderThanTwentyFourHours()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto { Id = 7 });
        var collectedAt = new DateTime(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);
        sut.Metrics.Add(new ServerMetricDto { ServerId = 7, Timestamp = collectedAt.AddHours(-25) });
        var heartbeat = new ServerHeartbeatDto
        {
            StorageDiagnostics = new StorageDiagnosticsDto
            {
                BuildCacheAvailable = true,
                DockerInventoryAvailable = true,
                BuildCacheBytes = 123,
                LastBuildAttemptAtUtc = collectedAt.AddMinutes(-5),
                CollectedAtUtc = collectedAt
            }
        };

        typeof(ServerDetailLoader).GetMethod("AppendMetric", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(sut, [heartbeat]);

        var metric = Assert.Single(sut.Metrics);
        Assert.Equal(7, metric.ServerId);
        Assert.True(metric.BuildCacheAvailable);
        Assert.True(metric.DockerInventoryAvailable);
        Assert.Equal(123, metric.BuildCacheBytes);
        Assert.Equal(collectedAt.AddMinutes(-5), metric.LastBuildAttemptAtUtc);
        Assert.Equal(collectedAt.ToLocalTime(), metric.Timestamp);
    }

    [Fact]
    public void ToLocalClock_ConvertsUtcToExplicitNegativeOffset()
    {
        var utc = new DateTime(2026, 7, 17, 2, 0, 0, DateTimeKind.Utc);
        var utcMinusSeven = TimeZoneInfo.CreateCustomTimeZone(
            "UTC-07-test",
            TimeSpan.FromHours(-7),
            "UTC-07-test",
            "UTC-07-test");

        var local = ServerDetailLoader.ToLocalClock(utc, utcMinusSeven);

        Assert.Equal(new DateTime(2026, 7, 16, 19, 0, 0), local);
    }

    [Fact]
    public async Task DisposeAsync_ClearsAndUnsubscribes()
    {
        var sut = CreateLoader();
        SetServer(sut, new ServerDetailDto { Id = 1 });

        await sut.DisposeAsync();

        Assert.Null(sut.Server);
        // Already unsubscribed - navigation must not throw.
        Services.GetRequiredService<NavigationManager>().NavigateTo("/projects");
    }
}
