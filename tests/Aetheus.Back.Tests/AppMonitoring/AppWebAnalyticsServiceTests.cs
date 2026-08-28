// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.AppMonitoring;
using Aetheus.Back.Components.AppMonitoring.Ingest;
using Aetheus.Back.Components.Notifications;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Aetheus.Back.Tests.AppMonitoring;

public sealed class AppWebAnalyticsServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly AppWebAnalyticsService _service;
    private readonly FakeTimeProvider _time =
        new(new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero));

    public AppWebAnalyticsServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        _db.MonitoredApps.Add(new MonitoredApp
        {
            Id = 1,
            ProjectId = 1,
            Name = "portfolio",
            Enabled = true,
            AnalyticsEnabled = true,
            AnalyticsPseudonymKeyVersion = 1,
            AnalyticsPseudonymKeyCreatedAt = _time.GetUtcNow().UtcDateTime,
            AnalyticsStorageBudgetBytes = 104_857_600
        });
        _db.SaveChanges();
        _service = new AppWebAnalyticsService(
            new AppWebAnalyticsRepository(_db),
            new AppMonitoringRepository(_db),
            Substitute.For<INotificationService>(),
            new AppIngestGate(),
            new ConfigurationBuilder().Build(),
            _time);
    }

    [Fact]
    public async Task Ingest_DeduplicatesPeriodsWithoutSummingDailyUniques()
    {
        var first = Event(Guid.NewGuid(), "a", "week-a", "month-a", "session-a");
        var second = Event(Guid.NewGuid(), "b", "week-a", "month-a", "session-b");

        var outcome = await _service.IngestAsync(1, [first, second], TestContext.Current.CancellationToken);
        var summary = await _service.GetSummaryAsync(1, 30, TestContext.Current.CancellationToken);

        Assert.Equal(2, outcome.Accepted);
        Assert.NotNull(summary);
        Assert.Equal(2, summary.UniqueVisitorsToday);
        Assert.Equal(1, summary.UniqueVisitorsThisWeek);
        Assert.Equal(1, summary.UniqueVisitorsThisMonth);
        Assert.Equal(2, summary.SessionsThisMonth);
        Assert.Equal(2, summary.PageViewsThisMonth);
    }

    [Fact]
    public async Task Ingest_ReplayIsRejectedWithoutChangingCounts()
    {
        var analyticsEvent = Event(Guid.NewGuid(), "a", "week-a", "month-a", "session-a");

        await _service.IngestAsync(1, [analyticsEvent], TestContext.Current.CancellationToken);
        var replay = await _service.IngestAsync(1, [analyticsEvent], TestContext.Current.CancellationToken);
        var summary = await _service.GetSummaryAsync(1, 30, TestContext.Current.CancellationToken);

        Assert.Equal(1, replay.Replayed);
        Assert.Equal(1, summary!.PageViewsThisMonth);
        Assert.Equal(1, summary.RejectedEvents);
    }

    [Fact]
    public async Task Ingest_SplitsAuthenticatedSessionAfterThirtyMinutesAndMarksReturn()
    {
        await _service.IngestAsync(
            1,
            [Event(Guid.NewGuid(), "a", "week-a", "month-a", "session-a") with
            {
                AuthenticatedPseudonym = Hash("account-a")
            }],
            TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromMinutes(31));
        await _service.IngestAsync(
            1,
            [Event(Guid.NewGuid(), "a", "week-a", "month-a", "session-a") with
            {
                OccurredAtUtc = _time.GetUtcNow().UtcDateTime,
                AuthenticatedPseudonym = Hash("account-a")
            }],
            TestContext.Current.CancellationToken);
        var summary = await _service.GetSummaryAsync(1, 30, TestContext.Current.CancellationToken);

        Assert.Equal(2, summary!.SessionsThisMonth);
        Assert.Equal(1, summary.ReturningVisitorsThisMonth);
    }

    [Fact]
    public async Task Ingest_AnonymousRepeatDoesNotCreateReturningVisitorProfile()
    {
        await _service.IngestAsync(
            1,
            [Event(Guid.NewGuid(), "a", "week-a", "month-a", "session-a")],
            TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromMinutes(31));
        await _service.IngestAsync(
            1,
            [Event(Guid.NewGuid(), "a", "week-a", "month-a", "session-a") with
            {
                OccurredAtUtc = _time.GetUtcNow().UtcDateTime
            }],
            TestContext.Current.CancellationToken);

        var summary = await _service.GetSummaryAsync(
            1,
            30,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, summary!.SessionsThisMonth);
        Assert.Equal(0, summary.ReturningVisitorsThisMonth);
    }

    [Fact]
    public async Task Ingest_RejectsHistoricalKeyWhenRotationTimestampIsUnavailable()
    {
        var app = await _db.MonitoredApps.SingleAsync(TestContext.Current.CancellationToken);
        app.AnalyticsPseudonymKeyVersion = 2;
        app.AnalyticsPseudonymKeyCreatedAt = null;
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var historical = await _service.IngestAsync(
            1,
            [Event(Guid.NewGuid(), "a", "week-a", "month-a", "session-a")],
            TestContext.Current.CancellationToken);
        var current = await _service.IngestAsync(
            1,
            [Event(Guid.NewGuid(), "b", "week-b", "month-b", "session-b") with { KeyVersion = 2 }],
            TestContext.Current.CancellationToken);

        Assert.Equal(1, historical.Rejected);
        Assert.Equal(0, historical.Accepted);
        Assert.Equal(1, current.Accepted);
    }

    [Fact]
    public async Task Ingest_StoresOptInBrowserSignalsWithoutInflatingAudienceStatistics()
    {
        var browserPerformance = Event(
            Guid.NewGuid(),
            "a",
            "week-a",
            "month-a",
            "session-a") with
        {
            Kind = "browser_performance",
            DurationMs = 124
        };
        var browserError = Event(
            Guid.NewGuid(),
            "a",
            "week-a",
            "month-a",
            "session-a") with
        {
            Kind = "browser_error",
            ErrorType = "script_error"
        };

        var outcome = await _service.IngestAsync(
            1,
            [browserPerformance, browserError],
            TestContext.Current.CancellationToken);
        var summary = await _service.GetSummaryAsync(1, 30, TestContext.Current.CancellationToken);
        var stored = await _db.AppAnalyticsEvents
            .OrderBy(item => item.Kind)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, outcome.Accepted);
        Assert.Equal(0, summary!.PageViewsThisMonth);
        Assert.Equal(0, summary.UniqueVisitorsThisMonth);
        Assert.Equal(0, summary.SessionsThisMonth);
        Assert.Equal(1, summary.BrowserPerformanceSamplesLast30Days);
        Assert.Equal(124, summary.AverageBrowserNavigationDurationMs);
        Assert.Equal(124, summary.P95BrowserNavigationDurationMs);
        Assert.Equal(1, summary.BrowserErrorsLast30Days);
        Assert.Equal(2, stored.Count);
        Assert.Contains(stored, item => item.DurationMs == 124 && item.ErrorType is null);
        Assert.Contains(stored, item => item.ErrorType == "script_error" && item.DurationMs is null);
    }

    [Fact]
    public async Task Ingest_RejectsBrowserErrorDetailsOutsideFixedAllowList()
    {
        var unsafeError = Event(
            Guid.NewGuid(),
            "a",
            "week-a",
            "month-a",
            "session-a") with
        {
            Kind = "browser_error",
            ErrorType = "email_alice_example_com"
        };

        var outcome = await _service.IngestAsync(
            1,
            [unsafeError],
            TestContext.Current.CancellationToken);

        Assert.Equal(0, outcome.Accepted);
        Assert.Equal(1, outcome.Rejected);
        Assert.Empty(_db.AppAnalyticsEvents);
    }

    [Fact]
    public async Task Ingest_AtStorageSaturation_DropsWithoutPersistingAudienceEvent()
    {
        var app = await _db.MonitoredApps.SingleAsync(TestContext.Current.CancellationToken);
        app.AnalyticsStorageBudgetBytes = 1;
        _db.AppAnalyticsRejections.Add(new AppAnalyticsRejection
        {
            MonitoredAppId = 1,
            OccurredAtUtc = _time.GetUtcNow().UtcDateTime.AddMinutes(-1),
            ReasonCode = "seed",
            Count = 1
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var outcome = await _service.IngestAsync(
            1,
            [Event(Guid.NewGuid(), "a", "week-a", "month-a", "session-a")],
            TestContext.Current.CancellationToken);

        Assert.Equal(0, outcome.Accepted);
        Assert.Equal(1, outcome.Rejected);
        Assert.Empty(_db.AppAnalyticsEvents);
        Assert.Equal(95, app.AnalyticsQuotaAlertLevel);
    }

    public void Dispose() => _db.Dispose();

    private AppWebAnalyticsIngestEvent Event(
        Guid eventId,
        string daily,
        string weekly,
        string monthly,
        string session) => new()
        {
            SchemaVersion = 1,
            ApplicationId = 1,
            SiteId = "portfolio",
            EventId = eventId,
            OccurredAtUtc = _time.GetUtcNow().UtcDateTime,
            Kind = "page_view",
            Route = "/projects/{id}",
            DailyPseudonym = Hash(daily),
            WeeklyPseudonym = Hash(weekly),
            MonthlyPseudonym = Hash(monthly),
            SessionPseudonym = Hash(session),
            KeyVersion = 1
        };

    private static string Hash(string value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
