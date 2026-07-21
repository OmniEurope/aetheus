// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.AppMonitoring;
using Aetheus.Back.Components.AppMonitoring.Ingest;
using Aetheus.Back.Components.Auth;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Aetheus.Back.Tests.AppMonitoring;

public class AppTelemetryServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly AppTelemetryService _sut;
    private readonly IIngestService _ingestService = Substitute.For<IIngestService>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 10, 12, 30, 0, TimeSpan.Zero));

    public AppTelemetryServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        var hasher = new IngestKeyHasher(Options.Create(new JwtOptions { SigningKey = "unit-test-signing-key-least-32-bytes!!" }));
        _sut = new AppTelemetryService(
            new AppMonitoringRepository(_db), new AppMetricRepository(_db),
            new AppLogRepository(_db), new AppErrorRepository(_db),
            hasher, _ingestService, _time);
        _db.MonitoredApps.Add(new MonitoredApp { Id = 1, ProjectId = 1, Name = "app" });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetMetricSeries_RawWindow_DecimatesToBoundedPointCount()
    {
        var now = _time.GetUtcNow().UtcDateTime;
        for (var i = 0; i < 5000; i++)
            _db.AppMetricSamples.Add(new AppMetricSample { MonitoredAppId = 1, MetricName = "cpu", Timestamp = now.AddSeconds(-i), Value = i });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var series = await _sut.GetMetricSeriesAsync(1, "cpu", 24, ct: TestContext.Current.CancellationToken);

        Assert.InRange(series.Points.Count, 1, 1500);
    }

    [Fact]
    public async Task GetMetricSeries_LongWindow_AppendsRecentRawTailPastRollup()
    {
        var now = _time.GetUtcNow().UtcDateTime; // 2026-01-10 12:30
        // One rolled-up hour at 08:00 plus a raw sample in the current (not-yet-aggregated) hour.
        _db.AppMetricHourly.Add(new AppMetricHourly
        {
            MonitoredAppId = 1,
            MetricName = "cpu",
            HourUtc = now.Date.AddHours(8),
            SampleCount = 10,
            MinValue = 1,
            MaxValue = 9,
            AvgValue = 5,
            P95Value = 8
        });
        _db.AppMetricSamples.Add(new AppMetricSample { MonitoredAppId = 1, MetricName = "cpu", Timestamp = now.AddMinutes(-5), Value = 42 });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var series = await _sut.GetMetricSeriesAsync(1, "cpu", 24 * 7, ct: TestContext.Current.CancellationToken);

        // The recent raw point must survive even though it is newer than the last rollup hour.
        Assert.Contains(series.Points, p => p.Value == 42);
        Assert.Contains(series.Points, p => p.P95 == 8); // the rollup point is still there too
    }

    [Fact]
    public async Task GenerateIngestKey_RotatesPersistedHash_AndInvalidatesOldAndNewCacheEntries()
    {
        var app = await _db.MonitoredApps.SingleAsync(a => a.Id == 1, cancellationToken: TestContext.Current.CancellationToken);
        app.IngestKeyHash = "old-hash";
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var generated = await _sut.GenerateIngestKeyAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(generated);
        Assert.False(string.IsNullOrWhiteSpace(generated.Key));
        Assert.Equal(_time.GetUtcNow().UtcDateTime, generated.CreatedAt);
        await _db.Entry(app).ReloadAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(app.IngestKeyHash);
        Assert.NotEqual("old-hash", app.IngestKeyHash);
        Assert.Equal(generated.CreatedAt, app.IngestKeyCreatedAt);
        _ingestService.Received(1).InvalidateKeyCache("old-hash");
        _ingestService.Received(1).InvalidateKeyCache(app.IngestKeyHash!);
        Assert.Null(await _sut.GenerateIngestKeyAsync(999, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RevokeIngestKey_ClearsPersistenceAndInvalidatesRevokedHash()
    {
        var app = await _db.MonitoredApps.SingleAsync(a => a.Id == 1, cancellationToken: TestContext.Current.CancellationToken);
        app.IngestKeyHash = "revoked-hash";
        app.IngestKeyCreatedAt = _time.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(await _sut.RevokeIngestKeyAsync(1, ct: TestContext.Current.CancellationToken));

        await _db.Entry(app).ReloadAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(app.IngestKeyHash);
        Assert.Null(app.IngestKeyCreatedAt);
        _ingestService.Received(1).InvalidateKeyCache("revoked-hash");
        Assert.False(await _sut.RevokeIngestKeyAsync(999, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MetricNamesAndRawSeries_MapDistinctNamesUnitAndLatestPoint()
    {
        var now = _time.GetUtcNow().UtcDateTime;
        _db.AppMetricSamples.AddRange(
            new AppMetricSample { MonitoredAppId = 1, MetricName = "cpu", Timestamp = now.AddMinutes(-2), Value = 20, Unit = "%" },
            new AppMetricSample { MonitoredAppId = 1, MetricName = "cpu", Timestamp = now.AddMinutes(-1), Value = 30, Unit = "%" },
            new AppMetricSample { MonitoredAppId = 1, MetricName = "memory", Timestamp = now, Value = 1024, Unit = "MiB" });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var names = await _sut.GetMetricNamesAsync(1, ct: TestContext.Current.CancellationToken);
        var series = await _sut.GetMetricSeriesAsync(1, "cpu", 0, ct: TestContext.Current.CancellationToken);

        Assert.Equal(["cpu", "memory"], names.OrderBy(name => name));
        Assert.Equal("%", series.Unit);
        Assert.Equal([20d, 30d], series.Points.Select(point => point.Value));
    }

    [Fact]
    public async Task ThresholdLifecycle_MapsPersistsAndDeletesObservableValues()
    {
        var created = await _sut.CreateThresholdAsync(1, new CreateMetricThresholdRequest
        {
            MetricName = "  cpu.utilization  ",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 85,
            Enabled = true
        }, ct: TestContext.Current.CancellationToken);

        var listed = await _sut.GetThresholdsAsync(1, ct: TestContext.Current.CancellationToken);
        Assert.Equal("cpu.utilization", created.MetricName);
        Assert.Equal(85, created.Threshold);
        Assert.Contains(listed, item => item.Id == created.Id && item.Operator == ComparisonOperator.GreaterThan);
        Assert.Equal(1, await _sut.GetThresholdAppIdAsync(created.Id, ct: TestContext.Current.CancellationToken));
        Assert.True(await _sut.DeleteThresholdAsync(created.Id, ct: TestContext.Current.CancellationToken));
        Assert.Empty(await _sut.GetThresholdsAsync(1, ct: TestContext.Current.CancellationToken));
        Assert.False(await _sut.DeleteThresholdAsync(999, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LogsAndErrors_ClampPaginationAndMapStoredTelemetry()
    {
        var now = _time.GetUtcNow().UtcDateTime;
        _db.AppLogEntries.Add(new AppLogEntry
        {
            MonitoredAppId = 1,
            Timestamp = now.AddMinutes(-2),
            SeverityNumber = 17,
            SeverityText = "ERROR",
            Body = "database unavailable",
            AttributesJson = "{\"node\":\"db-1\"}"
        });
        _db.AppErrorEvents.Add(new AppErrorEvent
        {
            Id = 14,
            MonitoredAppId = 1,
            Fingerprint = "fp",
            ExceptionType = "TimeoutException",
            Message = "probe timed out",
            TopFrame = "Probe.Run",
            OccurrenceCount = 3,
            FirstSeenAt = now.AddHours(-1),
            LastSeenAt = now
        });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var logs = await _sut.GetLogsAsync(1, hours: 500, minSeverity: 10, search: "database", page: 0, pageSize: 500, ct: TestContext.Current.CancellationToken);
        var errors = await _sut.GetErrorsAsync(1, page: 0, pageSize: 500, ct: TestContext.Current.CancellationToken);

        Assert.Equal(1, logs.Page);
        Assert.Equal(200, logs.PageSize);
        var log = Assert.Single(logs.Items);
        Assert.Equal("database unavailable", log.Body);
        Assert.Equal("ERROR", log.SeverityText);
        Assert.Equal(1, errors.Page);
        Assert.Equal(200, errors.PageSize);
        var error = Assert.Single(errors.Items);
        Assert.Equal(14, error.Id);
        Assert.Equal(3, error.OccurrenceCount);
        Assert.Equal("Probe.Run", error.TopFrame);
    }
}
