// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.AppMonitoring;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests.AppMonitoring;

public sealed class AppMonitoringSummaryRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly AppMonitoringRepository _repository;

    public AppMonitoringSummaryRepositoryTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        _repository = new AppMonitoringRepository(_db);
    }

    [Fact]
    public async Task GetActiveVisitorCounts_ReturnsDistinctRecentSessionsForRequestedApps()
    {
        var cutoff = new DateTime(2026, 8, 11, 10, 0, 0, DateTimeKind.Utc);
        _db.AppAnalyticsSessions.AddRange(
            Session(1, "active-a", cutoff.AddMinutes(1)),
            Session(1, "active-b", cutoff.AddMinutes(2)),
            Session(1, "stale", cutoff.AddSeconds(-1)),
            Session(2, "other-app", cutoff.AddMinutes(1)));
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var counts = await _repository.GetActiveVisitorCountsAsync(
            [1], cutoff, TestContext.Current.CancellationToken);

        Assert.Equal(2, Assert.Single(counts).Value);
    }

    public void Dispose() => _db.Dispose();

    private static AppAnalyticsSession Session(int appId, string pseudonym, DateTime lastSeenAtUtc) => new()
    {
        MonitoredAppId = appId,
        SessionPseudonym = pseudonym,
        StartedAtUtc = lastSeenAtUtc.AddMinutes(-1),
        LastSeenAtUtc = lastSeenAtUtc,
        KeyVersion = 1
    };
}
