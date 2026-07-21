// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Components.ServiceConnections;

public class ServiceConnectionRepository(AppDbContext db) : IServiceConnectionRepository
{
    public async Task<(List<ServiceConnection> Items, int TotalCount)> GetPagedAsync(
        string? search, int? projectId, int page, int pageSize, List<int>? accessibleIds = null, CancellationToken ct = default)
    {
        var query = db.ServiceConnections.AsNoTracking().AsQueryable();

        if (accessibleIds is not null)
            query = query.Where(sc => accessibleIds.Contains(sc.Id));

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(sc => sc.Name.Contains(search));

        if (projectId.HasValue)
            query = query.Where(sc => sc.ProjectId == projectId);

        var totalCount = await query.CountAsync(ct).ConfigureAwait(false);

        var items = await query
            .Include(sc => sc.Project)
            .OrderBy(sc => sc.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct).ConfigureAwait(false);

        return (items, totalCount);
    }

    public async Task<ServiceConnection?> GetDetailAsync(int id, CancellationToken ct = default)
    {
        return await db.ServiceConnections
            .AsNoTracking()
            .Include(sc => sc.Project)
            .FirstOrDefaultAsync(sc => sc.Id == id, ct).ConfigureAwait(false);
    }

    public async Task<ServiceConnection?> FindAsync(int id, CancellationToken ct = default)
    {
        return await db.ServiceConnections.FindAsync([id], ct).ConfigureAwait(false);
    }

    public async Task<List<ServiceConnection>> FindByNamesAsync(List<string> names, int? projectId, CancellationToken ct = default)
    {
        return await db.ServiceConnections
            .AsNoTracking()
            .Where(sc => names.Contains(sc.Name) && (sc.ProjectId == null || sc.ProjectId == projectId))
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task AddAsync(ServiceConnection connection, CancellationToken ct = default)
    {
        db.ServiceConnections.Add(connection);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(ServiceConnection connection, CancellationToken ct = default)
    {
        db.ServiceConnections.Remove(connection);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
