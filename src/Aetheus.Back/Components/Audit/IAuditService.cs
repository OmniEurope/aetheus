// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Audit;


public interface IAuditService
{
    Task LogAsync(string action, string entityType, int? entityId = null, string? details = null, CancellationToken ct = default);
    Task<PaginatedResult<AuditLogDto>> GetLogsPagedAsync(int page, int pageSize, string? search = null, string? action = null, string? entityType = null, int? entityId = null, DateTime? dateFrom = null, DateTime? dateTo = null, CancellationToken ct = default,
        string? sortBy = null, bool sortDescending = true, IReadOnlyList<GridFilter>? filters = null);
    Task<List<string>> GetDistinctActionsAsync(CancellationToken ct = default);
    Task<List<string>> GetDistinctEntityTypesAsync(CancellationToken ct = default);
    Task<HashSet<string>> GetRecentLoginIpsAsync(string username, int maxEntries, CancellationToken ct = default);
}
