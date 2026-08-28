// SPDX-License-Identifier: EUPL-1.2
using System.Data;
using System.Globalization;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Aetheus.Back.Components.AppMonitoring;

public sealed class AppWebAnalyticsRepository(AppDbContext db) : IAppWebAnalyticsRepository
{
    private const int EventEstimatedBytes = 480;
    private const int SessionEstimatedBytes = 320;
    private const int IdentityEstimatedBytes = 240;
    private const int AggregateEstimatedBytes = 192;
    private const int PageEstimatedBytes = 256;
    private const int RejectionEstimatedBytes = 128;

    public async Task<(int Accepted, int Replayed)> RecordAsync(
        int appId,
        IReadOnlyList<AppWebAnalyticsIngestEvent> events,
        int sessionTimeoutMinutes,
        DateTime receivedAtUtc,
        CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            IDbContextTransaction? transaction = null;
            try
            {
                if (db.Database.IsRelational())
                    transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
                        .ConfigureAwait(false);
                var result = await RecordBatchAsync(
                    appId, events, sessionTimeoutMinutes, receivedAtUtc, ct).ConfigureAwait(false);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                if (transaction is not null)
                    await transaction.CommitAsync(ct).ConfigureAwait(false);
                return result;
            }
            catch (Exception ex) when (attempt < 4 && IsConcurrencyConflict(ex))
            {
                if (transaction is not null)
                    await transaction.RollbackAsync(ct).ConfigureAwait(false);
                db.ChangeTracker.Clear();
            }
            finally
            {
                if (transaction is not null)
                    await transaction.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task<(int Accepted, int Replayed)> RecordBatchAsync(
        int appId,
        IReadOnlyList<AppWebAnalyticsIngestEvent> events,
        int sessionTimeoutMinutes,
        DateTime receivedAtUtc,
        CancellationToken ct)
    {
        var batchIds = new HashSet<Guid>();
        var ordered = events.OrderBy(item => item.OccurredAtUtc)
            .Where(item => batchIds.Add(item.EventId))
            .ToList();
        var duplicateCount = events.Count - ordered.Count;
        var eventIds = ordered.Select(item => item.EventId).ToList();
        var existingIds = await db.AppAnalyticsEvents.AsNoTracking()
            .Where(item => item.MonitoredAppId == appId && eventIds.Contains(item.EventId))
            .Select(item => item.EventId)
            .ToListAsync(ct).ConfigureAwait(false);
        var existingIdSet = existingIds.ToHashSet();
        ordered.RemoveAll(item => existingIdSet.Contains(item.EventId));

        var pageViews = ordered.Where(item => item.Kind == "page_view").ToList();
        var sessionPseudonyms = pageViews.Select(item => item.SessionPseudonym).Distinct().ToList();
        var keyVersions = pageViews.Select(item => item.KeyVersion).Distinct().ToList();
        var earliestSessionFloor = pageViews.Count == 0
            ? receivedAtUtc
            : pageViews.Min(item => item.OccurredAtUtc).AddMinutes(-sessionTimeoutMinutes);
        await db.AppAnalyticsSessions
            .Where(item => item.MonitoredAppId == appId
                           && sessionPseudonyms.Contains(item.SessionPseudonym)
                           && keyVersions.Contains(item.KeyVersion)
                           && item.LastSeenAtUtc >= earliestSessionFloor)
            .LoadAsync(ct).ConfigureAwait(false);
        var returningPseudonyms = (await db.AppAnalyticsSessions.AsNoTracking()
                .Where(item => item.MonitoredAppId == appId
                               && sessionPseudonyms.Contains(item.SessionPseudonym)
                               && keyVersions.Contains(item.KeyVersion)
                               && item.LastSeenAtUtc < earliestSessionFloor)
                .Select(item => new { item.SessionPseudonym, item.KeyVersion })
                .Distinct()
                .ToListAsync(ct).ConfigureAwait(false))
            .Select(item => (item.SessionPseudonym, item.KeyVersion))
            .ToHashSet();

        var periodStarts = pageViews.SelectMany(Periods)
            .Select(item => item.Start).Distinct().ToList();
        var periodPseudonyms = pageViews.SelectMany(Periods)
            .Select(item => item.Pseudonym).Distinct().ToList();
        await db.AppAnalyticsPeriodIdentities
            .Where(item => item.MonitoredAppId == appId
                           && periodStarts.Contains(item.PeriodStartUtc)
                           && periodPseudonyms.Contains(item.Pseudonym))
            .LoadAsync(ct).ConfigureAwait(false);
        await db.AppAnalyticsAggregates
            .Where(item => item.MonitoredAppId == appId
                           && periodStarts.Contains(item.PeriodStartUtc))
            .LoadAsync(ct).ConfigureAwait(false);
        var days = pageViews.Select(item => DateOnly.FromDateTime(item.OccurredAtUtc)).Distinct().ToList();
        var routes = pageViews.Select(item => item.Route).Distinct().ToList();
        await db.AppAnalyticsPageAggregates
            .Where(item => item.MonitoredAppId == appId
                           && days.Contains(item.DayUtc)
                           && routes.Contains(item.Route))
            .LoadAsync(ct).ConfigureAwait(false);

        foreach (var analyticsEvent in ordered)
            RecordLoadedEvent(appId, analyticsEvent, sessionTimeoutMinutes, receivedAtUtc, returningPseudonyms);
        return (ordered.Count, duplicateCount + existingIds.Count);
    }

    private void RecordLoadedEvent(
        int appId,
        AppWebAnalyticsIngestEvent analyticsEvent,
        int sessionTimeoutMinutes,
        DateTime receivedAtUtc,
        HashSet<(string SessionPseudonym, int KeyVersion)> returningPseudonyms)
    {
        db.AppAnalyticsEvents.Add(new AppAnalyticsEvent
        {
            MonitoredAppId = appId,
            EventId = analyticsEvent.EventId,
            OccurredAtUtc = analyticsEvent.OccurredAtUtc,
            Kind = analyticsEvent.Kind,
            Route = analyticsEvent.Route,
            DurationMs = analyticsEvent.DurationMs,
            ErrorType = analyticsEvent.ErrorType,
            SessionPseudonym = analyticsEvent.SessionPseudonym,
            Authenticated = analyticsEvent.AuthenticatedPseudonym is not null,
            KeyVersion = analyticsEvent.KeyVersion
        });
        if (!string.Equals(analyticsEvent.Kind, "page_view", StringComparison.Ordinal))
            return;

        var floor = analyticsEvent.OccurredAtUtc.AddMinutes(-sessionTimeoutMinutes);
        var ceiling = analyticsEvent.OccurredAtUtc.AddMinutes(sessionTimeoutMinutes);
        var session = db.AppAnalyticsSessions.Local
            .Where(item => item.MonitoredAppId == appId
                           && item.SessionPseudonym == analyticsEvent.SessionPseudonym
                           && item.KeyVersion == analyticsEvent.KeyVersion
                           && item.LastSeenAtUtc >= floor
                           && item.StartedAtUtc <= ceiling)
            .OrderByDescending(item => item.LastSeenAtUtc)
            .FirstOrDefault();
        var newSession = session is null;
        if (session is null)
        {
            var returning = analyticsEvent.AuthenticatedPseudonym is not null
                            && (returningPseudonyms.Contains((
                                    analyticsEvent.SessionPseudonym,
                                    analyticsEvent.KeyVersion))
                                || db.AppAnalyticsSessions.Local.Any(item =>
                                    item.MonitoredAppId == appId
                                    && item.SessionPseudonym == analyticsEvent.SessionPseudonym
                                    && item.KeyVersion == analyticsEvent.KeyVersion
                                    && item.LastSeenAtUtc < floor));
            session = new AppAnalyticsSession
            {
                MonitoredAppId = appId,
                SessionPseudonym = analyticsEvent.SessionPseudonym,
                StartedAtUtc = analyticsEvent.OccurredAtUtc,
                LastSeenAtUtc = analyticsEvent.OccurredAtUtc,
                Authenticated = analyticsEvent.AuthenticatedPseudonym is not null,
                ReturningVisitor = returning,
                KeyVersion = analyticsEvent.KeyVersion
            };
            db.AppAnalyticsSessions.Add(session);
        }
        if (analyticsEvent.OccurredAtUtc < session.StartedAtUtc)
            session.StartedAtUtc = analyticsEvent.OccurredAtUtc;
        if (analyticsEvent.OccurredAtUtc > session.LastSeenAtUtc)
            session.LastSeenAtUtc = analyticsEvent.OccurredAtUtc;
        session.PageViewCount++;

        foreach (var (kind, start, pseudonym) in Periods(analyticsEvent))
        {
            var identity = db.AppAnalyticsPeriodIdentities.Local.FirstOrDefault(item =>
                item.MonitoredAppId == appId
                && item.PeriodKind == kind
                && item.PeriodStartUtc == start
                && item.Pseudonym == pseudonym);
            var newIdentity = identity is null;
            if (identity is null)
            {
                identity = new AppAnalyticsPeriodIdentity
                {
                    MonitoredAppId = appId,
                    PeriodKind = kind,
                    PeriodStartUtc = start,
                    Pseudonym = pseudonym,
                    FirstSeenAtUtc = analyticsEvent.OccurredAtUtc,
                    KeyVersion = analyticsEvent.KeyVersion
                };
                db.AppAnalyticsPeriodIdentities.Add(identity);
            }
            identity.LastSeenAtUtc = analyticsEvent.OccurredAtUtc;

            var aggregate = db.AppAnalyticsAggregates.Local.FirstOrDefault(item =>
                item.MonitoredAppId == appId
                && item.PeriodKind == kind
                && item.PeriodStartUtc == start);
            if (aggregate is null)
            {
                aggregate = new AppAnalyticsAggregate
                {
                    MonitoredAppId = appId,
                    PeriodKind = kind,
                    PeriodStartUtc = start
                };
                db.AppAnalyticsAggregates.Add(aggregate);
            }
            aggregate.PageViews++;
            if (newIdentity)
            {
                aggregate.UniqueVisitors++;
                if (analyticsEvent.AuthenticatedPseudonym is not null)
                    aggregate.AuthenticatedUniqueVisitors++;
            }
            if (newSession)
            {
                aggregate.Sessions++;
                if (session.ReturningVisitor)
                    aggregate.ReturningVisitors++;
            }
            aggregate.UpdatedAtUtc = receivedAtUtc;
        }

        var day = DateOnly.FromDateTime(analyticsEvent.OccurredAtUtc);
        var page = db.AppAnalyticsPageAggregates.Local.FirstOrDefault(item =>
            item.MonitoredAppId == appId && item.DayUtc == day && item.Route == analyticsEvent.Route);
        if (page is null)
        {
            page = new AppAnalyticsPageAggregate
            {
                MonitoredAppId = appId,
                DayUtc = day,
                Route = analyticsEvent.Route
            };
            db.AppAnalyticsPageAggregates.Add(page);
        }
        page.PageViews++;
    }

    private static IEnumerable<(AnalyticsPeriodKind Kind, DateOnly Start, string Pseudonym)> Periods(
        AppWebAnalyticsIngestEvent analyticsEvent)
    {
        var instant = analyticsEvent.OccurredAtUtc;
        yield return (AnalyticsPeriodKind.Day, DateOnly.FromDateTime(instant), analyticsEvent.DailyPseudonym);
        yield return (AnalyticsPeriodKind.Week, WeekStart(instant), analyticsEvent.WeeklyPseudonym);
        yield return (AnalyticsPeriodKind.Month, new DateOnly(instant.Year, instant.Month, 1),
            analyticsEvent.MonthlyPseudonym);
    }

    private static bool IsConcurrencyConflict(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is PostgresException postgres
                && postgres.SqlState is PostgresErrorCodes.SerializationFailure
                    or PostgresErrorCodes.DeadlockDetected
                    or PostgresErrorCodes.UniqueViolation)
                return true;
        }
        return false;
    }

    public async Task<long> EstimateStorageBytesAsync(int appId, CancellationToken ct = default)
    {
        var events = await db.AppAnalyticsEvents.CountAsync(item => item.MonitoredAppId == appId, ct)
            .ConfigureAwait(false);
        var sessions = await db.AppAnalyticsSessions.CountAsync(item => item.MonitoredAppId == appId, ct)
            .ConfigureAwait(false);
        var identities = await db.AppAnalyticsPeriodIdentities.CountAsync(item => item.MonitoredAppId == appId, ct)
            .ConfigureAwait(false);
        var aggregates = await db.AppAnalyticsAggregates.CountAsync(item => item.MonitoredAppId == appId, ct)
            .ConfigureAwait(false);
        var pages = await db.AppAnalyticsPageAggregates.CountAsync(item => item.MonitoredAppId == appId, ct)
            .ConfigureAwait(false);
        var rejections = await db.AppAnalyticsRejections.CountAsync(item => item.MonitoredAppId == appId, ct)
            .ConfigureAwait(false);
        return (long)events * EventEstimatedBytes
               + (long)sessions * SessionEstimatedBytes
               + (long)identities * IdentityEstimatedBytes
               + (long)aggregates * AggregateEstimatedBytes
               + (long)pages * PageEstimatedBytes
               + (long)rejections * RejectionEstimatedBytes;
    }

    public async Task<HashSet<string>> GetRouteNamesAsync(int appId, CancellationToken ct = default)
    {
        var routes = await db.AppAnalyticsPageAggregates.AsNoTracking()
            .Where(item => item.MonitoredAppId == appId)
            .Select(item => item.Route)
            .Distinct()
            .ToListAsync(ct).ConfigureAwait(false);
        return routes.ToHashSet(StringComparer.Ordinal);
    }

    public async Task RecordRejectionAsync(
        int appId,
        string reasonCode,
        int count,
        DateTime occurredAtUtc,
        CancellationToken ct = default)
    {
        db.AppAnalyticsRejections.Add(new AppAnalyticsRejection
        {
            MonitoredAppId = appId,
            OccurredAtUtc = occurredAtUtc,
            ReasonCode = reasonCode,
            Count = count
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<AppWebAnalyticsSummaryDto> GetSummaryAsync(
        int appId,
        DateOnly todayUtc,
        DateOnly weekStartUtc,
        DateOnly monthStartUtc,
        DateOnly historyStartUtc,
        long storageBudgetBytes,
        long rejectedEvents,
        DateTime? lastIngestAtUtc,
        CancellationToken ct = default)
    {
        var aggregateRows = await db.AppAnalyticsAggregates.AsNoTracking()
            .Where(item => item.MonitoredAppId == appId
                           && (item.PeriodStartUtc >= historyStartUtc
                               || item.PeriodStartUtc == weekStartUtc
                               || item.PeriodStartUtc == monthStartUtc))
            .ToListAsync(ct).ConfigureAwait(false);
        var pages = await db.AppAnalyticsPageAggregates.AsNoTracking()
            .Where(item => item.MonitoredAppId == appId && item.DayUtc >= monthStartUtc)
            .GroupBy(item => item.Route)
            .Select(group => new AppWebAnalyticsPageDto
            {
                Route = group.Key,
                PageViews = group.Sum(item => item.PageViews)
            })
            .OrderByDescending(item => item.PageViews)
            .Take(20)
            .ToListAsync(ct).ConfigureAwait(false);
        var historyStartInstantUtc = historyStartUtc.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var browserSignals = await db.AppAnalyticsEvents.AsNoTracking()
            .Where(item => item.MonitoredAppId == appId
                           && item.OccurredAtUtc >= historyStartInstantUtc
                           && (item.Kind == "browser_performance" || item.Kind == "browser_error"))
            .Select(item => new { item.Kind, item.DurationMs })
            .ToListAsync(ct).ConfigureAwait(false);
        var browserDurations = browserSignals
            .Where(item => item.Kind == "browser_performance" && item.DurationMs.HasValue)
            .Select(item => item.DurationMs!.Value)
            .Order()
            .ToList();
        var p95Index = browserDurations.Count == 0
            ? -1
            : Math.Max(0, (int)Math.Ceiling(browserDurations.Count * 0.95) - 1);
        var storage = await EstimateStorageBytesAsync(appId, ct).ConfigureAwait(false);

        var day = aggregateRows.FirstOrDefault(item =>
            item.PeriodKind == AnalyticsPeriodKind.Day && item.PeriodStartUtc == todayUtc);
        var week = aggregateRows.FirstOrDefault(item =>
            item.PeriodKind == AnalyticsPeriodKind.Week && item.PeriodStartUtc == weekStartUtc);
        var month = aggregateRows.FirstOrDefault(item =>
            item.PeriodKind == AnalyticsPeriodKind.Month && item.PeriodStartUtc == monthStartUtc);
        var daily = aggregateRows
            .Where(item => item.PeriodKind == AnalyticsPeriodKind.Day && item.PeriodStartUtc >= historyStartUtc)
            .OrderBy(item => item.PeriodStartUtc)
            .Select(item => new AppWebAnalyticsPointDto
            {
                DayUtc = item.PeriodStartUtc,
                UniqueVisitors = item.UniqueVisitors,
                Sessions = item.Sessions,
                ReturningVisitors = item.ReturningVisitors,
                PageViews = item.PageViews
            })
            .ToList();

        return new AppWebAnalyticsSummaryDto
        {
            UniqueVisitorsToday = day?.UniqueVisitors ?? 0,
            UniqueVisitorsThisWeek = week?.UniqueVisitors ?? 0,
            UniqueVisitorsThisMonth = month?.UniqueVisitors ?? 0,
            AuthenticatedUniqueThisMonth = month?.AuthenticatedUniqueVisitors ?? 0,
            SessionsThisMonth = month?.Sessions ?? 0,
            ReturningVisitorsThisMonth = month?.ReturningVisitors ?? 0,
            PageViewsThisMonth = month?.PageViews ?? 0,
            BrowserPerformanceSamplesLast30Days = browserDurations.Count,
            AverageBrowserNavigationDurationMs = browserDurations.Count == 0
                ? null
                : browserDurations.Average(),
            P95BrowserNavigationDurationMs = p95Index < 0 ? null : browserDurations[p95Index],
            BrowserErrorsLast30Days = browserSignals.Count(item => item.Kind == "browser_error"),
            LastIngestAtUtc = lastIngestAtUtc,
            RejectedEvents = rejectedEvents,
            EstimatedStorageBytes = storage,
            StorageBudgetBytes = storageBudgetBytes,
            StorageUsagePercent = storageBudgetBytes <= 0
                ? 0
                : (int)Math.Min(100, storage * 100 / storageBudgetBytes),
            Daily = daily,
            TopPages = pages
        };
    }

    public async Task<WebAnalyticsPurgeResult> PurgeAsync(
        DateTime eventCutoffUtc,
        DateTime sessionCutoffUtc,
        DateOnly aggregateCutoffUtc,
        DateTime rejectionCutoffUtc,
        CancellationToken ct = default)
    {
        var events = await DeleteAsync(db.AppAnalyticsEvents.Where(item => item.OccurredAtUtc < eventCutoffUtc), ct)
            .ConfigureAwait(false);
        var sessions = await DeleteAsync(db.AppAnalyticsSessions.Where(item => item.LastSeenAtUtc < sessionCutoffUtc), ct)
            .ConfigureAwait(false);
        var identities = await DeleteAsync(
            db.AppAnalyticsPeriodIdentities.Where(item => item.LastSeenAtUtc < sessionCutoffUtc),
            ct).ConfigureAwait(false);
        var aggregates = await DeleteAsync(
            db.AppAnalyticsAggregates.Where(item => item.PeriodStartUtc < aggregateCutoffUtc),
            ct).ConfigureAwait(false);
        var pages = await DeleteAsync(
            db.AppAnalyticsPageAggregates.Where(item => item.DayUtc < aggregateCutoffUtc),
            ct).ConfigureAwait(false);
        var rejections = await DeleteAsync(
            db.AppAnalyticsRejections.Where(item => item.OccurredAtUtc < rejectionCutoffUtc),
            ct).ConfigureAwait(false);
        return new WebAnalyticsPurgeResult(events, sessions, identities, aggregates, pages, rejections);
    }

    private async Task<int> DeleteAsync<TEntity>(IQueryable<TEntity> query, CancellationToken ct)
        where TEntity : class
    {
        if (db.Database.IsRelational())
            return await query.ExecuteDeleteAsync(ct).ConfigureAwait(false);

        var rows = await query.ToListAsync(ct).ConfigureAwait(false);
        db.RemoveRange(rows);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return rows.Count;
    }

    internal static DateOnly WeekStart(DateTime instant)
    {
        var day = DateOnly.FromDateTime(instant);
        var offset = ((int)instant.DayOfWeek + 6) % 7;
        return day.AddDays(-offset);
    }
}
