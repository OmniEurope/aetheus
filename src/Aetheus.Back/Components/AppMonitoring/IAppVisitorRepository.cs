// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.AppMonitoring;

public readonly record struct AppVisitorDayCount(DateOnly DayUtc, int UniqueVisitors);

public interface IAppVisitorRepository
{
    Task RecordAsync(int appId, DateOnly dayUtc, string fingerprintHash, DateTime firstSeenAt, CancellationToken ct = default);
    Task<List<AppVisitorDayCount>> GetDailyCountsAsync(int appId, DateOnly sinceUtc, CancellationToken ct = default);
    Task<int> PurgeOlderThanAsync(DateOnly cutoffUtc, CancellationToken ct = default);
}
