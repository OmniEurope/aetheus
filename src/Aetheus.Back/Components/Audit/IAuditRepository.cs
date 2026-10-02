// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Audit;

public interface IAuditRepository
{
    Task AddAsync(AuditLog log, CancellationToken ct = default);
    Task<List<AuditLog>> GetPagedAsync(int skip, int take, string? search = null, string? action = null, string? entityType = null, int? entityId = null, DateTime? dateFrom = null, DateTime? dateTo = null, CancellationToken ct = default,
        string? sortBy = null, bool sortDescending = true, IReadOnlyList<GridFilter>? filters = null);
    Task<int> CountAsync(string? search = null, string? action = null, string? entityType = null, int? entityId = null, DateTime? dateFrom = null, DateTime? dateTo = null, CancellationToken ct = default,
        IReadOnlyList<GridFilter>? filters = null);
    Task<List<string>> GetDistinctActionsAsync(CancellationToken ct = default);
    Task<List<string>> GetDistinctEntityTypesAsync(CancellationToken ct = default);
    Task<string> GetLastHashAsync(CancellationToken ct = default);
    IAsyncEnumerable<AuditLog> StreamOrderedAsync();
    Task<HashSet<string>> GetRecentLoginIpsAsync(string username, int maxEntries, CancellationToken ct = default);
}
