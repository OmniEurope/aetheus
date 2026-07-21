// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.AppMonitoring;
using Aetheus.Back.Components.Notifications;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Aetheus.Back.Tests.AppMonitoring;

public sealed class AppMonitoringServiceCrudTests
{
    private readonly IAppMonitoringRepository _repo = Substitute.For<IAppMonitoringRepository>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IEntityChangeNotifier _notifier = Substitute.For<IEntityChangeNotifier>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));
    private readonly AppMonitoringService _service;

    public AppMonitoringServiceCrudTests()
    {
        _repo.GetProjectOrgIdsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyCollection<int>>().ToDictionary(id => id, _ => 9));
        _repo.GetRawUptimeAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns((0, 0));
        _repo.GetHourlyUptimeAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns((0, 0));
        _service = new AppMonitoringService(
            _repo, _notifications, _notifier, _time, NullLogger<AppMonitoringService>.Instance);
    }

    [Fact]
    public async Task GetAppsForProject_MapsNavigationFieldsAndThreeHonestUptimeWindows()
    {
        var app = new MonitoredApp
        {
            Id = 2,
            ProjectId = 4,
            Name = "api",
            ProbeUrl = "https://api.example/health",
            Project = new Project { Id = 4, Name = "Platform" },
            Environment = new Aetheus.Back.Data.Entities.Environment { Id = 5, Name = "prod" },
            EnvironmentId = 5,
            Server = new Server { Id = 7, Name = "web-01" },
            ServerId = 7,
            IngestKeyHash = "hash",
            IngestDroppedCount = 3
        };
        _repo.GetAppsByProjectAsync(4, Arg.Any<CancellationToken>()).Returns([app]);
        _repo.GetRawUptimeAsync(2, Arg.Is<DateTime>(d => d == _time.GetUtcNow().UtcDateTime.AddHours(-24)), Arg.Any<CancellationToken>())
            .Returns((18, 20));
        _repo.GetRawUptimeAsync(2, Arg.Is<DateTime>(d => d == _time.GetUtcNow().UtcDateTime.AddDays(-7)), Arg.Any<CancellationToken>())
            .Returns((60, 80));
        _repo.GetHourlyUptimeAsync(2, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns((900, 1000));

        var dto = Assert.Single(await _service.GetAppsForProjectAsync(4, ct: TestContext.Current.CancellationToken));

        Assert.Equal("Platform", dto.ProjectName);
        Assert.Equal("prod", dto.EnvironmentName);
        Assert.Equal("web-01", dto.ServerName);
        Assert.Equal(0.9, dto.Uptime24h);
        Assert.Equal(0.75, dto.Uptime7d);
        Assert.Equal(0.9, dto.Uptime90d);
        Assert.True(dto.HasIngestKey);
        Assert.Equal(3, dto.IngestDroppedCount);
    }

    [Fact]
    public async Task GetAppAndProjectId_MissingAndExistingPathsPreserveRepositorySemantics()
    {
        _repo.GetAppAsync(99, Arg.Any<CancellationToken>()).Returns((MonitoredApp?)null);
        _repo.GetAppAsync(2, Arg.Any<CancellationToken>())
            .Returns(new MonitoredApp { Id = 2, ProjectId = 4, Name = "api" });
        _repo.GetAppProjectIdAsync(2, Arg.Any<CancellationToken>()).Returns(4);

        Assert.Null(await _service.GetAppAsync(99, ct: TestContext.Current.CancellationToken));
        Assert.Equal("api", (await _service.GetAppAsync(2, ct: TestContext.Current.CancellationToken))!.Name);
        Assert.Equal(4, await _service.GetAppProjectIdAsync(2, ct: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("relative/path")]
    [InlineData("ftp://example.com")]
    public async Task CreateApp_InvalidProbeUrl_RejectsBeforePersistence(string url)
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _service.CreateAppAsync(4,
            new CreateMonitoredAppRequest { Name = "api", ProjectId = 4, ProbeUrl = url }, ct: TestContext.Current.CancellationToken));
        await _repo.DidNotReceive().AddAppAsync(Arg.Any<MonitoredApp>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateApp_ValidRequest_TrimsClampsPersistsAndBroadcastsWithinOrganization()
    {
        MonitoredApp? persisted = null;
        _repo.AddAppAsync(Arg.Any<MonitoredApp>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            persisted = call.Arg<MonitoredApp>();
            persisted.Id = 12;
            return Task.CompletedTask;
        });

        var dto = await _service.CreateAppAsync(4, new CreateMonitoredAppRequest
        {
            ProjectId = 4,
            Name = "  api  ",
            ProbeUrl = "  https://api.example/health  ",
            ProbeIntervalSeconds = 1,
            ProbeTimeoutSeconds = 500,
            ExpectedStatusCode = 999,
            FailureThreshold = 0,
            RecoveryThreshold = 99,
            Enabled = true
        }, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(persisted);
        Assert.Equal("api", persisted.Name);
        Assert.Equal("https://api.example/health", persisted.ProbeUrl);
        Assert.Equal(15, persisted.ProbeIntervalSeconds);
        Assert.Equal(120, persisted.ProbeTimeoutSeconds);
        Assert.Equal(599, persisted.ExpectedStatusCode);
        Assert.Equal(1, persisted.FailureThreshold);
        Assert.Equal(20, persisted.RecoveryThreshold);
        Assert.Equal(AppHealthStatus.Unknown, persisted.CurrentStatus);
        Assert.Equal(12, dto.Id);
        await _notifier.Received(1).BroadcastAsync(ResourceType.Project, 4, EntityChangeOps.Created, Arg.Any<CancellationToken>(), 9);
    }

    [Fact]
    public async Task UpdateApp_MissingReturnsNull_ExistingMutatesAndBroadcasts()
    {
        _repo.GetAppForUpdateAsync(99, Arg.Any<CancellationToken>()).Returns((MonitoredApp?)null);
        Assert.Null(await _service.UpdateAppAsync(99, new UpdateMonitoredAppRequest { Name = "missing" }, ct: TestContext.Current.CancellationToken));

        var app = new MonitoredApp { Id = 2, ProjectId = 4, Name = "old" };
        _repo.GetAppForUpdateAsync(2, Arg.Any<CancellationToken>()).Returns(app);
        var dto = await _service.UpdateAppAsync(2, new UpdateMonitoredAppRequest
        {
            Name = "  renamed ",
            ProbeUrl = " ",
            ProbeIntervalSeconds = 4000,
            ProbeTimeoutSeconds = 0,
            ExpectedStatusCode = 50,
            FailureThreshold = 30,
            RecoveryThreshold = 0,
            Enabled = false
        }, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(dto);
        Assert.Equal("renamed", app.Name);
        Assert.Null(app.ProbeUrl);
        Assert.Equal(3600, app.ProbeIntervalSeconds);
        Assert.Equal(1, app.ProbeTimeoutSeconds);
        Assert.Equal(100, app.ExpectedStatusCode);
        Assert.Equal(20, app.FailureThreshold);
        Assert.Equal(1, app.RecoveryThreshold);
        Assert.False(app.Enabled);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _notifier.Received(1).BroadcastAsync(ResourceType.Project, 4, EntityChangeOps.Updated, Arg.Any<CancellationToken>(), 9);
    }

    [Fact]
    public async Task DeleteApp_ExistingRemovesAndBroadcasts_MissingReturnsFalse()
    {
        _repo.GetAppForUpdateAsync(99, Arg.Any<CancellationToken>()).Returns((MonitoredApp?)null);
        Assert.False(await _service.DeleteAppAsync(99, ct: TestContext.Current.CancellationToken));

        var app = new MonitoredApp { Id = 2, ProjectId = 4 };
        _repo.GetAppForUpdateAsync(2, Arg.Any<CancellationToken>()).Returns(app);
        Assert.True(await _service.DeleteAppAsync(2, ct: TestContext.Current.CancellationToken));

        await _repo.Received(1).RemoveAppAsync(app, Arg.Any<CancellationToken>());
        await _notifier.Received(1).BroadcastAsync(ResourceType.Project, 4, EntityChangeOps.Deleted, Arg.Any<CancellationToken>(), 9);
    }

    [Fact]
    public async Task GetSamples_ClampsWindowAndMapsEveryObservableField()
    {
        var expectedSince = _time.GetUtcNow().UtcDateTime.AddHours(-168);
        _repo.GetSamplesSinceAsync(2, expectedSince, Arg.Any<CancellationToken>()).Returns(
        [
            new AppHealthSample
            {
                MonitoredAppId = 2, Timestamp = _time.GetUtcNow().UtcDateTime,
                IsUp = false, ResponseTimeMs = 250, StatusCode = 503, Error = "unavailable"
            }
        ]);

        var sample = Assert.Single(await _service.GetSamplesAsync(2, 999, ct: TestContext.Current.CancellationToken));

        Assert.False(sample.IsUp);
        Assert.Equal(250, sample.ResponseTimeMs);
        Assert.Equal(503, sample.StatusCode);
        Assert.Equal("unavailable", sample.Error);
    }

    [Fact]
    public async Task ProbeConfigsAndServerIdMap_DelegateWithoutLosingOwnershipData()
    {
        _repo.GetAppsForServerAsync(7, Arg.Any<CancellationToken>()).Returns(
        [
            new MonitoredApp
            {
                Id = 2, ProbeUrl = null, ProbeIntervalSeconds = 30,
                ProbeTimeoutSeconds = 4, ExpectedStatusCode = 204
            }
        ]);
        var ownership = new Dictionary<int, int?> { [2] = 7, [3] = null };
        _repo.GetAppServerIdsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>()).Returns(ownership);

        var config = Assert.Single(await _service.GetProbeConfigsForServerAsync(7, ct: TestContext.Current.CancellationToken));
        var mappedOwnership = await _service.GetAppServerIdsAsync([2, 3], ct: TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, config.ProbeUrl);
        Assert.Equal(204, config.ExpectedStatusCode);
        Assert.Same(ownership, mappedOwnership);
    }

    [Fact]
    public async Task GetSummary_EmptyScopeShortCircuits_OtherwiseBuildsHistogramAndTroubledOrder()
    {
        var empty = await _service.GetSummaryAsync([], ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, empty.TotalCount);
        await _repo.DidNotReceive().GetAppsForSummaryAsync(Arg.Any<List<int>?>(), Arg.Any<CancellationToken>());

        var apps = new List<MonitoredApp>
        {
            new() { Id = 1, ProjectId = 4, Name = "up", CurrentStatus = AppHealthStatus.Up },
            new() { Id = 2, ProjectId = 4, Name = "down-old", CurrentStatus = AppHealthStatus.Down, LastStatusChangeAt = _time.GetUtcNow().UtcDateTime.AddHours(-2) },
            new() { Id = 3, ProjectId = 5, Name = "degraded-new", CurrentStatus = AppHealthStatus.Degraded, LastStatusChangeAt = _time.GetUtcNow().UtcDateTime.AddMinutes(-5), Project = new Project { Name = "Web" } },
            new() { Id = 4, ProjectId = 5, Name = "unknown", CurrentStatus = AppHealthStatus.Unknown }
        };
        _repo.GetAppsForSummaryAsync(null, Arg.Any<CancellationToken>()).Returns(apps);

        var summary = await _service.GetSummaryAsync(null, ct: TestContext.Current.CancellationToken);

        Assert.Equal(4, summary.TotalCount);
        Assert.Equal(1, summary.UpCount);
        Assert.Equal(1, summary.DownCount);
        Assert.Equal(1, summary.DegradedCount);
        Assert.Equal(1, summary.UnknownCount);
        Assert.Equal(["degraded-new", "down-old"], summary.Troubled.Select(x => x.Name));
        Assert.Equal("Web", summary.Troubled[0].ProjectName);
    }
}
