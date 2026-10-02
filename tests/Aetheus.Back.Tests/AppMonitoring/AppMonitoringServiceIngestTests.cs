// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.AppMonitoring;
using Aetheus.Back.Components.Notifications;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Aetheus.Back.Tests.AppMonitoring;

public class AppMonitoringServiceIngestTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly AppMonitoringRepository _repo;
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IEntityChangeNotifier _notifier = Substitute.For<IEntityChangeNotifier>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero));
    private readonly AppMonitoringService _service;

    public AppMonitoringServiceIngestTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new AppMonitoringRepository(_db);
        _service = new AppMonitoringService(_repo, _notifications, _notifier, _time,
            Substitute.For<ILogger<AppMonitoringService>>());

        _db.Projects.Add(new Project { Id = 1, Name = "p", OrganizationId = 7 });
        _db.MonitoredApps.Add(new MonitoredApp
        {
            Id = 1,
            ProjectId = 1,
            Name = "app",
            ServerId = 5,
            ProbeUrl = "http://x",
            FailureThreshold = 2,
            RecoveryThreshold = 2,
            CurrentStatus = AppHealthStatus.Unknown
        });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static AppProbeResultDto Result(bool up, DateTime ts) =>
        new() { MonitoredAppId = 1, IsUp = up, Timestamp = ts, ResponseTimeMs = up ? 100 : null };

    [Fact]
    public async Task Ingest_TwoFailures_DeclaresDown_AndNotifies()
    {
        var t0 = _time.GetUtcNow().UtcDateTime;
        var applied = await _service.IngestProbeResultsAsync(
        [
            Result(false, t0),
            Result(false, t0.AddSeconds(30))
        ], ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, applied);
        var app = await _db.MonitoredApps.AsNoTracking().SingleAsync(a => a.Id == 1, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(AppHealthStatus.Down, app.CurrentStatus);
        Assert.NotNull(app.LastStatusChangeAt);
        Assert.Equal(2, await _db.AppHealthSamples.CountAsync(cancellationToken: TestContext.Current.CancellationToken));

        await _notifications.Received(1).SendEventAsync("app.down", Arg.Any<object>(), Arg.Any<CancellationToken>());
        await _notifier.Received().BroadcastAsync(ResourceType.Project, 1, EntityChangeOps.Updated,
            Arg.Any<CancellationToken>(), 7);
    }

    [Fact]
    public async Task Ingest_SingleUpFromUnknown_StaysUnknown_NoNotification()
    {
        var t0 = _time.GetUtcNow().UtcDateTime;
        await _service.IngestProbeResultsAsync([Result(true, t0)], ct: TestContext.Current.CancellationToken);

        var app = await _db.MonitoredApps.AsNoTracking().SingleAsync(a => a.Id == 1, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(AppHealthStatus.Unknown, app.CurrentStatus); // recovery=2 -> one Up not enough (no-fake)
        await _notifications.DidNotReceive().SendEventAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ingest_RecoveryAfterDown_NotifiesRecovered()
    {
        // Arrange: drive it Down first.
        var t0 = _time.GetUtcNow().UtcDateTime;
        await _service.IngestProbeResultsAsync([Result(false, t0), Result(false, t0.AddSeconds(30))], ct: TestContext.Current.CancellationToken);
        _notifications.ClearReceivedCalls();

        // Act: two successes recover it.
        await _service.IngestProbeResultsAsync(
        [
            Result(true, t0.AddSeconds(60)),
            Result(true, t0.AddSeconds(90))
        ], ct: TestContext.Current.CancellationToken);

        var app = await _db.MonitoredApps.AsNoTracking().SingleAsync(a => a.Id == 1, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(AppHealthStatus.Up, app.CurrentStatus);
        await _notifications.Received(1).SendEventAsync("app.recovered", Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ingest_ForeignAppId_IsSkipped()
    {
        var t0 = _time.GetUtcNow().UtcDateTime;
        var applied = await _service.IngestProbeResultsAsync([new AppProbeResultDto { MonitoredAppId = 999, IsUp = false, Timestamp = t0 }], ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, applied);
        Assert.Equal(0, await _db.AppHealthSamples.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }
}
