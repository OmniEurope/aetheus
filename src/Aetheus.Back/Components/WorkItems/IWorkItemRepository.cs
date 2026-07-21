// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Components.WorkItems;

public interface IWorkItemRepository
{
    Task<(List<WorkItem> Items, int TotalCount)> GetWorkItemsPagedAsync(
        int? projectId, string? search, string? sortBy, bool sortDescending,
        int page, int pageSize, WorkItemType? type = null,
        WorkItemStatus? status = null, int? assigneeUserId = null,
        CancellationToken ct = default);
    Task<WorkItem?> GetWorkItemDetailAsync(int id, CancellationToken ct = default);
    Task<WorkItem?> FindWorkItemAsync(int id, CancellationToken ct = default);
    Task AddWorkItemAsync(WorkItem workItem, CancellationToken ct = default);
    Task RemoveWorkItemAsync(WorkItem workItem, CancellationToken ct = default);
    Task<List<WorkItem>> GetBoardItemsAsync(int projectId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
