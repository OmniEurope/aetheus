// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Components.WorkItems;

public interface IWorkItemService
{
    Task<PaginatedResult<WorkItemDto>> GetWorkItemsAsync(WorkItemPaginationRequest request, CancellationToken ct = default);
    Task<WorkItemDetailDto?> GetWorkItemDetailAsync(int id, CancellationToken ct = default);
    Task<WorkItemDto> CreateWorkItemAsync(CreateWorkItemRequest request, CancellationToken ct = default);
    Task<WorkItemDto?> UpdateWorkItemAsync(int id, UpdateWorkItemRequest request, CancellationToken ct = default);
    Task<WorkItemDto?> MoveWorkItemAsync(int id, MoveWorkItemRequest request, CancellationToken ct = default);
    Task<bool> DeleteWorkItemAsync(int id, CancellationToken ct = default);
    Task<List<WorkItemBoardColumn>> GetBoardAsync(int projectId, CancellationToken ct = default);

    /// <summary>F-03: resolve the owning ProjectId for a work item (for RBAC).</summary>
    Task<int?> GetProjectIdForItemAsync(int workItemId, CancellationToken ct = default);
}
