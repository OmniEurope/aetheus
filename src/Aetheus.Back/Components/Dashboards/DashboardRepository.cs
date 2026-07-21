// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Components.Dashboards;

public class DashboardRepository(AppDbContext db) : IDashboardRepository
{
    public async Task<List<Dashboard>> GetByUserIdAsync(int userId, CancellationToken ct = default)
    {
        return await db.Dashboards
            .AsNoTracking()
            .Where(d => d.UserId == userId)
            .Include(d => d.Widgets)
            .OrderBy(d => d.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<Dashboard?> GetDetailAsync(int id, CancellationToken ct = default)
    {
        return await db.Dashboards
            .AsNoTracking()
            .Include(d => d.Widgets)
            .FirstOrDefaultAsync(d => d.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<Dashboard?> FindAsync(int id, CancellationToken ct = default)
    {
        return await db.Dashboards
            .Include(d => d.Widgets)
            .FirstOrDefaultAsync(d => d.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(Dashboard dashboard, CancellationToken ct = default)
    {
        db.Dashboards.Add(dashboard);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Dashboard dashboard, CancellationToken ct = default)
    {
        db.Dashboards.Remove(dashboard);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task ClearDefaultsAsync(int userId, CancellationToken ct = default)
    {
        if (db.Database.IsRelational())
        {
            await db.Dashboards
                .Where(d => d.UserId == userId && d.IsDefault)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.IsDefault, false), ct)
                .ConfigureAwait(false);
        }
        else
        {
            // InMemory fallback - ExecuteUpdate not supported
            var defaults = await db.Dashboards
                .Where(d => d.UserId == userId && d.IsDefault)
                .ToListAsync(ct).ConfigureAwait(false);
            foreach (var d in defaults) d.IsDefault = false;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
