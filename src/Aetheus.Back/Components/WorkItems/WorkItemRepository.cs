// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Components.WorkItems;

public class WorkItemRepository(AppDbContext db) : IWorkItemRepository
{
    public async Task<(List<WorkItem> Items, int TotalCount)> GetWorkItemsPagedAsync(
        int? projectId, string? search, string? sortBy, bool sortDescending,
        int page, int pageSize, WorkItemType? type = null,
        WorkItemStatus? status = null, int? assigneeUserId = null,
        CancellationToken ct = default)
    {
        var query = db.WorkItems.AsNoTracking().AsQueryable();

        if (projectId.HasValue)
            query = query.Where(w => w.ProjectId == projectId.Value);
        if (type.HasValue)
            query = query.Where(w => w.Type == type.Value);
        if (status.HasValue)
            query = query.Where(w => w.Status == status.Value);
        if (assigneeUserId.HasValue)
            query = query.Where(w => w.AssigneeUserId == assigneeUserId.Value);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(w => w.Title.Contains(search));

        var totalCount = await query.CountAsync(ct).ConfigureAwait(false);

        query = sortBy?.ToLowerInvariant() switch
        {
            "priority" => sortDescending ? query.OrderByDescending(w => w.Priority) : query.OrderBy(w => w.Priority),
            "status" => sortDescending ? query.OrderByDescending(w => w.Status) : query.OrderBy(w => w.Status),
            "type" => sortDescending ? query.OrderByDescending(w => w.Type) : query.OrderBy(w => w.Type),
            "createdat" => sortDescending ? query.OrderByDescending(w => w.CreatedAt) : query.OrderBy(w => w.CreatedAt),
            _ => sortDescending ? query.OrderByDescending(w => w.Order) : query.OrderBy(w => w.Order)
        };

        var items = await query
            .Include(w => w.Assignee)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return (items, totalCount);
    }

    public async Task<WorkItem?> GetWorkItemDetailAsync(int id, CancellationToken ct = default)
    {
        return await db.WorkItems
            .AsNoTracking()
            .Where(w => w.Id == id)
            .Include(w => w.Assignee)
            .Include(w => w.Children)
            .AsSplitQuery()
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<WorkItem?> FindWorkItemAsync(int id, CancellationToken ct = default)
    {
        return await db.WorkItems.FindAsync([id], ct).ConfigureAwait(false);
    }

    public async Task AddWorkItemAsync(WorkItem workItem, CancellationToken ct = default)
    {
        db.WorkItems.Add(workItem);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveWorkItemAsync(WorkItem workItem, CancellationToken ct = default)
    {
        db.WorkItems.Remove(workItem);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // The kanban board is intrinsically unpaginated (it renders every column at once), but a hard
    // safety cap keeps one abnormally large project from materializing an unbounded result set (audit).
    private const int BoardItemsSafetyCap = 5000;

    public async Task<List<WorkItem>> GetBoardItemsAsync(int projectId, CancellationToken ct = default)
    {
        return await db.WorkItems
            .AsNoTracking()
            .Where(w => w.ProjectId == projectId)
            .Include(w => w.Assignee)
            .OrderBy(w => w.Order)
            .Take(BoardItemsSafetyCap)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
