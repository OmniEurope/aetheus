// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.AppMonitoring;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests.AppMonitoring;

public sealed class AppWebAnalyticsRetentionTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly AppWebAnalyticsRepository _repository;

    public AppWebAnalyticsRetentionTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        _repository = new AppWebAnalyticsRepository(_db);
    }

    [Fact]
    public async Task Purge_AppliesDetailSessionAggregateAndDiagnosticTiersAcrossTwentyFiveMonths()
    {
        var now = new DateTime(2026, 7, 23, 12, 0, 0, DateTimeKind.Utc);
        var staleAggregateDate = DateOnly.FromDateTime(now.AddMonths(-26));
        var retainedAggregateDate = DateOnly.FromDateTime(now.AddMonths(-24));
        _db.AppAnalyticsEvents.AddRange(
            Event(now.AddDays(-31)),
            Event(now.AddDays(-29)));
        _db.AppAnalyticsSessions.AddRange(
            Session(now.AddDays(-91)),
            Session(now.AddDays(-89)));
        _db.AppAnalyticsPeriodIdentities.AddRange(
            Identity(now.AddDays(-91)),
            Identity(now.AddDays(-89)));
        _db.AppAnalyticsAggregates.AddRange(
            Aggregate(staleAggregateDate),
            Aggregate(retainedAggregateDate));
        _db.AppAnalyticsPageAggregates.AddRange(
            Page(staleAggregateDate),
            Page(retainedAggregateDate));
        _db.AppAnalyticsRejections.AddRange(
            Rejection(now.AddDays(-8)),
            Rejection(now.AddDays(-6)));
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _repository.PurgeAsync(
            now.AddDays(-30),
            now.AddDays(-90),
            DateOnly.FromDateTime(now.AddMonths(-25)),
            now.AddDays(-7),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Events);
        Assert.Equal(1, result.Sessions);
        Assert.Equal(1, result.PeriodIdentities);
        Assert.Equal(1, result.Aggregates);
        Assert.Equal(1, result.Pages);
        Assert.Equal(1, result.Rejections);
        Assert.Single(_db.AppAnalyticsEvents);
        Assert.Single(_db.AppAnalyticsSessions);
        Assert.Single(_db.AppAnalyticsPeriodIdentities);
        Assert.Single(_db.AppAnalyticsAggregates);
        Assert.Single(_db.AppAnalyticsPageAggregates);
        Assert.Single(_db.AppAnalyticsRejections);
    }

    public void Dispose() => _db.Dispose();

    private static AppAnalyticsEvent Event(DateTime occurredAtUtc) => new()
    {
        MonitoredAppId = 1,
        EventId = Guid.NewGuid(),
        OccurredAtUtc = occurredAtUtc,
        Kind = "page_view",
        Route = "/",
        SessionPseudonym = Hash(),
        KeyVersion = 1
    };

    private static AppAnalyticsSession Session(DateTime lastSeenAtUtc) => new()
    {
        MonitoredAppId = 1,
        SessionPseudonym = Hash(),
        StartedAtUtc = lastSeenAtUtc.AddMinutes(-1),
        LastSeenAtUtc = lastSeenAtUtc,
        KeyVersion = 1
    };

    private static AppAnalyticsPeriodIdentity Identity(DateTime lastSeenAtUtc) => new()
    {
        MonitoredAppId = 1,
        PeriodKind = AnalyticsPeriodKind.Day,
        PeriodStartUtc = DateOnly.FromDateTime(lastSeenAtUtc),
        Pseudonym = Hash(),
        FirstSeenAtUtc = lastSeenAtUtc,
        LastSeenAtUtc = lastSeenAtUtc,
        KeyVersion = 1
    };

    private static AppAnalyticsAggregate Aggregate(DateOnly periodStartUtc) => new()
    {
        MonitoredAppId = 1,
        PeriodKind = AnalyticsPeriodKind.Month,
        PeriodStartUtc = periodStartUtc,
        UpdatedAtUtc = periodStartUtc.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
    };

    private static AppAnalyticsPageAggregate Page(DateOnly dayUtc) => new()
    {
        MonitoredAppId = 1,
        DayUtc = dayUtc,
        Route = $"/{dayUtc:yyyy-MM}"
    };

    private static AppAnalyticsRejection Rejection(DateTime occurredAtUtc) => new()
    {
        MonitoredAppId = 1,
        OccurredAtUtc = occurredAtUtc,
        ReasonCode = "test",
        Count = 1
    };

    private static string Hash() => Convert.ToHexString(
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
}
