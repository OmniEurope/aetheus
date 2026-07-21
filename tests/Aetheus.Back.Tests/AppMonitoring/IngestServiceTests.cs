// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.AppMonitoring;
using Aetheus.Back.Components.AppMonitoring.Ingest;
using Aetheus.Back.Components.Auth;
using Aetheus.Back.Components.Notifications;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Aetheus.Back.Tests.AppMonitoring;

public class IngestServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IngestService _service;
    private readonly IngestKeyHasher _hasher;
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();

    public IngestServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        var appRepo = new AppMonitoringRepository(_db);
        var metricRepo = new AppMetricRepository(_db);
        _hasher = new IngestKeyHasher(Options.Create(new JwtOptions { SigningKey = "unit-test-signing-key-least-32-bytes!!" }));
        _service = new IngestService(appRepo, metricRepo, new AppLogRepository(_db), new AppErrorRepository(_db),
            _hasher, new MemoryCache(new MemoryCacheOptions()), _notifications, new AppIngestGate(),
            new FakeTimeProvider(new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero)),
            Substitute.For<ILogger<IngestService>>());

        _db.MonitoredApps.Add(new MonitoredApp { Id = 1, ProjectId = 1, Name = "app", IngestKeyHash = _hasher.Hash("secret-key") });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task ResolveAppId_MatchesHashedKey_RejectsUnknown()
    {
        Assert.Equal(1, await _service.ResolveAppIdAsync("secret-key", ct: TestContext.Current.CancellationToken));
        Assert.Null(await _service.ResolveAppIdAsync("wrong-key", ct: TestContext.Current.CancellationToken));
        Assert.Null(await _service.ResolveAppIdAsync("", ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InvalidateKeyCache_EvictsMapping_SoRevokedKeyStopsResolving()
    {
        // Prime the resolver cache (hash -> appId 1).
        Assert.Equal(1, await _service.ResolveAppIdAsync("secret-key", ct: TestContext.Current.CancellationToken));

        // Revoke the key in the store, as AppTelemetryService.RevokeIngestKeyAsync does.
        var app = _db.MonitoredApps.Single(a => a.Id == 1);
        app.IngestKeyHash = null;
        _db.SaveChanges();

        // Without eviction the cached mapping would keep resolving for the whole TTL (the bug).
        Assert.Equal(1, await _service.ResolveAppIdAsync("secret-key", ct: TestContext.Current.CancellationToken));

        // After eviction the revoked key stops resolving immediately.
        _service.InvalidateKeyCache(_hasher.Hash("secret-key"));
        Assert.Null(await _service.ResolveAppIdAsync("secret-key", ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IngestMetrics_PersistsSamples_AndTouchesApp()
    {
        var points = new List<ParsedMetricPoint>
        {
            new("cpu", 12.5, "%", default, null),
            new("mem", 512, "MB", default, "{\"host\":\"a\"}")
        };
        var outcome = await _service.IngestMetricsAsync(1, points, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, outcome.Accepted);
        Assert.Equal(0, outcome.Dropped);
        Assert.Equal(2, await _db.AppMetricSamples.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
        var app = await _db.MonitoredApps.AsNoTracking().SingleAsync(a => a.Id == 1, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(app.LastIngestAt);
    }

    [Fact]
    public async Task IngestMetrics_CardinalityCap_DropsExcessNewNames()
    {
        // 201 distinct new metric names, cap is 200 -> exactly one dropped, counted on the app.
        var points = Enumerable.Range(0, IngestService.MaxMetricNamesPerApp + 1)
            .Select(i => new ParsedMetricPoint($"metric_{i}", i, null, default, null))
            .ToList();

        var outcome = await _service.IngestMetricsAsync(1, points, ct: TestContext.Current.CancellationToken);

        Assert.Equal(IngestService.MaxMetricNamesPerApp, outcome.Accepted);
        Assert.Equal(1, outcome.Dropped);
        var app = await _db.MonitoredApps.AsNoTracking().SingleAsync(a => a.Id == 1, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, app.IngestDroppedCount);
    }

    [Fact]
    public async Task IngestMetrics_SustainedRateCap_DropsExcessWithinTheMinute()
    {
        // S-TECH-VOLM: same metric name (no cardinality interference), all timestamped "now" (in window).
        // Push in per-request-capped batches until the per-minute ceiling is crossed: the overflow is
        // dropped honestly and counted, and no more than the ceiling is persisted.
        const int batch = IngestService.MaxPointsPerRequest; // 5000
        var batches = (IngestService.MaxSamplesPerMinutePerApp / batch) + 1; // one batch past the ceiling
        var totalDropped = 0;
        var totalAccepted = 0;
        for (var b = 0; b < batches; b++)
        {
            var points = Enumerable.Range(0, batch)
                .Select(i => new ParsedMetricPoint("cpu", i, "%", default, null))
                .ToList();
            var outcome = await _service.IngestMetricsAsync(1, points, ct: TestContext.Current.CancellationToken);
            totalAccepted += outcome.Accepted;
            totalDropped += outcome.Dropped;
        }

        Assert.Equal(IngestService.MaxSamplesPerMinutePerApp, totalAccepted);
        Assert.Equal(IngestService.MaxSamplesPerMinutePerApp, await _db.AppMetricSamples.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(batches * batch - IngestService.MaxSamplesPerMinutePerApp, totalDropped);
    }

    [Fact]
    public async Task IngestMetrics_OutOfWindowTimestamp_IsDropped()
    {
        // now = 2026-01-10 12:00Z (FakeTimeProvider). A point dated a year in the future would fall outside
        // both the aggregation window and the retention purge -> permanent orphan row. It must be dropped.
        var future = new DateTime(2027, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        var ancient = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var outcome = await _service.IngestMetricsAsync(1,
        [
            new ParsedMetricPoint("cpu", 1, "%", future, null),
            new ParsedMetricPoint("cpu", 2, "%", ancient, null)
        ], ct: TestContext.Current.CancellationToken);

        Assert.Equal(0, outcome.Accepted);
        Assert.Equal(2, outcome.Dropped);
        Assert.Equal(0, await _db.AppMetricSamples.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IngestMetrics_ThresholdBreach_FiresOnce_ThenRecovers()
    {
        _db.AppMetricThresholds.Add(new AppMetricThreshold
        {
            MonitoredAppId = 1,
            MetricName = "cpu",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 80,
            Enabled = true
        });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _service.IngestMetricsAsync(1, [new ParsedMetricPoint("cpu", 90, "%", default, null)], ct: TestContext.Current.CancellationToken);
        await _notifications.Received(1).SendEventAsync("app.metric.threshold", Arg.Any<object>(), Arg.Any<CancellationToken>());
        Assert.True(await _db.AppMetricThresholds.AsNoTracking().Select(t => t.IsBreached).SingleAsync(cancellationToken: TestContext.Current.CancellationToken));

        // Still breached -> no duplicate notification.
        await _service.IngestMetricsAsync(1, [new ParsedMetricPoint("cpu", 95, "%", default, null)], ct: TestContext.Current.CancellationToken);
        await _notifications.Received(1).SendEventAsync("app.metric.threshold", Arg.Any<object>(), Arg.Any<CancellationToken>());

        // Back under threshold -> clears the breach.
        await _service.IngestMetricsAsync(1, [new ParsedMetricPoint("cpu", 40, "%", default, null)], ct: TestContext.Current.CancellationToken);
        Assert.False(await _db.AppMetricThresholds.AsNoTracking().Select(t => t.IsBreached).SingleAsync(cancellationToken: TestContext.Current.CancellationToken));
    }
}
