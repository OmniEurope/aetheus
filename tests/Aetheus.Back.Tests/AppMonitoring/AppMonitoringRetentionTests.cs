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
    public async Task AggregateHour_ComputesBucket_AndIsIdempotent()
    {
        var hour = new DateTime(2026, 1, 10, 10, 0, 0, DateTimeKind.Utc); // completed hour

        _db.AppHealthSamples.AddRange(
            new AppHealthSample { MonitoredAppId = 1, Timestamp = hour.AddMinutes(15), IsUp = true, ResponseTimeMs = 100 },
            new AppHealthSample { MonitoredAppId = 1, Timestamp = hour.AddMinutes(30), IsUp = true, ResponseTimeMs = 200 },
            new AppHealthSample { MonitoredAppId = 1, Timestamp = hour.AddMinutes(45), IsUp = false, ResponseTimeMs = null });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var written = await _repo.AggregateHourAsync(hour, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, written);

        var agg = await _db.AppHealthHourly.AsNoTracking().SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(hour, agg.HourUtc);
        Assert.Equal(3, agg.SampleCount);
        Assert.Equal(2, agg.UpCount);
        Assert.Equal(150, agg.AvgResponseTimeMs);

        // Idempotent: re-running the same window must not duplicate the bucket.
        var again = await _repo.AggregateHourAsync(hour, ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, again);
        Assert.Equal(1, await _db.AppHealthHourly.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AggregateHour_IgnoresSamplesOfTheNextHour()
    {
        var currentHourStart = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        // A sample inside the current (incomplete) hour is not part of the completed hour before it.
        _db.AppHealthSamples.Add(new AppHealthSample { MonitoredAppId = 1, Timestamp = currentHourStart.AddMinutes(5), IsUp = true, ResponseTimeMs = 50 });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var written = await _repo.AggregateHourAsync(currentHourStart.AddHours(-1), ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, written);
        Assert.Equal(0, await _db.AppHealthHourly.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task R2020_AggregateHour_RollsUpEachApplicationOfTheHourOnItsOwn()
    {
        var hour = new DateTime(2026, 1, 10, 10, 0, 0, DateTimeKind.Utc);
        _db.MonitoredApps.Add(new MonitoredApp { Id = 2, ProjectId = 1, Name = "other" });
        _db.AppHealthSamples.AddRange(
            new AppHealthSample { MonitoredAppId = 1, Timestamp = hour.AddMinutes(5), IsUp = true, ResponseTimeMs = 100 },
            new AppHealthSample { MonitoredAppId = 2, Timestamp = hour.AddMinutes(5), IsUp = false, ResponseTimeMs = null },
            new AppHealthSample { MonitoredAppId = 2, Timestamp = hour.AddMinutes(35), IsUp = true, ResponseTimeMs = 300 });
        // Application 1 is already rolled up for this hour: only application 2 is written.
        _db.AppHealthHourly.Add(new AppHealthHourly { MonitoredAppId = 1, HourUtc = hour, SampleCount = 1, UpCount = 1 });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, await _repo.AggregateHourAsync(hour, ct: TestContext.Current.CancellationToken));

        var other = await _db.AppHealthHourly.AsNoTracking()
            .SingleAsync(h => h.MonitoredAppId == 2, TestContext.Current.CancellationToken);
        Assert.Equal((2, 1, 300d), (other.SampleCount, other.UpCount, other.AvgResponseTimeMs));
    }

    [Fact]
    public async Task R2020_MetricAggregateHour_BackfillsAnOldHourWithoutReadingTheOthers()
    {
        var metricRepo = new AppMetricRepository(_db);
        var currentHourStart = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        var oldHour = currentHourStart.AddHours(-30); // beyond a 6h steady-state lookback, within a 48h catch-up
        _db.AppMetricSamples.AddRange(
            new AppMetricSample { MonitoredAppId = 1, MetricName = "cpu", Timestamp = oldHour.AddMinutes(10), Value = 5 },
            new AppMetricSample { MonitoredAppId = 1, MetricName = "cpu", Timestamp = currentHourStart.AddHours(-2), Value = 9 });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, await metricRepo.AggregateHourAsync(oldHour, ct: TestContext.Current.CancellationToken));

        var rollup = await _db.AppMetricHourly.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal((oldHour, 1, 5d), (rollup.HourUtc, rollup.SampleCount, rollup.AvgValue));
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

    [Fact]
    public async Task VisitorRepository_DeduplicatesDailyIdentity_AndPurgesExpiredDays()
    {
        var visitors = new AppVisitorRepository(_db);
        var today = new DateOnly(2026, 1, 10);
        var now = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        await visitors.RecordAsync(1, today, "same", now, TestContext.Current.CancellationToken);
        await visitors.RecordAsync(1, today, "same", now.AddMinutes(1), TestContext.Current.CancellationToken);
        await visitors.RecordAsync(1, today.AddDays(-40), "old", now.AddDays(-40), TestContext.Current.CancellationToken);

        Assert.Equal(2, await _db.AppVisitorIdentities.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(1, await visitors.PurgeOlderThanAsync(today.AddDays(-35), TestContext.Current.CancellationToken));
        Assert.Single(await visitors.GetDailyCountsAsync(1, today, TestContext.Current.CancellationToken));
    }
}
