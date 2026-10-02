// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.AppMonitoring;

public interface IAppLogRepository
{
    Task AddLogsAsync(IEnumerable<AppLogEntry> logs, CancellationToken ct = default);
    Task<(List<AppLogEntry> Items, int TotalCount)> GetLogsAsync(
        int appId, DateTime since, int? minSeverity, string? search, int page, int pageSize, CancellationToken ct = default,
        IReadOnlyList<GridFilter>? filters = null, string? sortBy = null, bool sortDescending = true);
    Task<int> PurgeOlderThanAsync(DateTime cutoff, CancellationToken ct = default);
}
