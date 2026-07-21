// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.AppMonitoring;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests.AppMonitoring;

public class AppMonitoringRetentionTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly AppMonitoringRepository _repo;

    public AppMonitoringRetentionTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new AppMonitoringRepository(_db);
        _db.MonitoredApps.Add(new MonitoredApp { Id = 1, ProjectId = 1, Name = "app" });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task AggregateRawIntoHourly_ComputesBucket_AndIsIdempotent()
    {
        var currentHourStart = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        var rawFloor = currentHourStart.AddDays(-7);
        var hour = new DateTime(2026, 1, 10, 10, 0, 0, DateTimeKind.Utc); // completed hour

        _db.AppHealthSamples.AddRange(
            new AppHealthSample { MonitoredAppId = 1, Timestamp = hour.AddMinutes(15), IsUp = true, ResponseTimeMs = 100 },
            new AppHealthSample { MonitoredAppId = 1, Timestamp = hour.AddMinutes(30), IsUp = true, ResponseTimeMs = 200 },
            new AppHealthSample { MonitoredAppId = 1, Timestamp = hour.AddMinutes(45), IsUp = false, ResponseTimeMs = null });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var written = await _repo.AggregateRawIntoHourlyAsync(currentHourStart, rawFloor, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, written);

        var agg = await _db.AppHealthHourly.AsNoTracking().SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(hour, agg.HourUtc);
        Assert.Equal(3, agg.SampleCount);
        Assert.Equal(2, agg.UpCount);
        Assert.Equal(150, agg.AvgResponseTimeMs);

        // Idempotent: re-running the same window must not duplicate the bucket.
        var again = await _repo.AggregateRawIntoHourlyAsync(currentHourStart, rawFloor, ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, again);
        Assert.Equal(1, await _db.AppHealthHourly.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AggregateRawIntoHourly_IgnoresCurrentIncompleteHour()
    {
        var currentHourStart = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        var rawFloor = currentHourStart.AddDays(-7);

        // Sample inside the current (incomplete) hour must not be aggregated yet.
        _db.AppHealthSamples.Add(new AppHealthSample { MonitoredAppId = 1, Timestamp = currentHourStart.AddMinutes(5), IsUp = true, ResponseTimeMs = 50 });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var written = await _repo.AggregateRawIntoHourlyAsync(currentHourStart, rawFloor, ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, written);
        Assert.Equal(0, await _db.AppHealthHourly.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AggregateRawIntoHourly_WideCatchUpFloor_BackfillsHoursMissedByShortLookback()
    {
        var metricRepo = new AppMetricRepository(_db);
        var currentHourStart = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        var oldHour = currentHourStart.AddHours(-30); // beyond a 6h steady-state lookback, within a 48h catch-up
        _db.AppMetricSamples.Add(new AppMetricSample
        {
            MonitoredAppId = 1,
            MetricName = "cpu",
            Timestamp = oldHour.AddMinutes(10),
            Value = 5
        });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        // The steady-state 6h lookback misses it - that is the permanent-gap bug on a long downtime.
        var shortFloor = currentHourStart.AddHours(-6);
        Assert.Equal(0, await metricRepo.AggregateRawIntoHourlyAsync(currentHourStart, shortFloor, ct: TestContext.Current.CancellationToken));

        // The startup catch-up widens the floor (48h) and backfills the missed hour.
        var catchUpFloor = currentHourStart.AddHours(-48);
        Assert.Equal(1, await metricRepo.AggregateRawIntoHourlyAsync(currentHourStart, catchUpFloor, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PurgeRawOlderThan_RemovesOnlyExpired()
    {
        var now = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        _db.AppHealthSamples.AddRange(
            new AppHealthSample { MonitoredAppId = 1, Timestamp = now.AddDays(-10), IsUp = true },  // expired
            new AppHealthSample { MonitoredAppId = 1, Timestamp = now.AddDays(-1), IsUp = true });   // fresh
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var purged = await _repo.PurgeRawOlderThanAsync(now.AddDays(-7), ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, purged);
        Assert.Equal(1, await _db.AppHealthSamples.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PurgeHourlyOlderThan_RemovesOnlyExpired()
    {
        var now = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        _db.AppHealthHourly.AddRange(
            new AppHealthHourly { MonitoredAppId = 1, HourUtc = now.AddDays(-100), SampleCount = 1, UpCount = 1 }, // expired
            new AppHealthHourly { MonitoredAppId = 1, HourUtc = now.AddDays(-1), SampleCount = 1, UpCount = 1 });   // fresh
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var purged = await _repo.PurgeHourlyOlderThanAsync(now.AddDays(-90), ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, purged);
        Assert.Equal(1, await _db.AppHealthHourly.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }
}
