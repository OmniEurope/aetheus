// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Components.ServerModules;

public class ServerModuleRepository(AppDbContext db) : IServerModuleRepository
{
    public async Task<List<ServerModule>> GetByServerIdAsync(int serverId, CancellationToken ct = default)
    {
        return await db.ServerModules
            .Where(m => m.ServerId == serverId)
            .AsNoTracking()
            .OrderBy(m => m.Name)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<ServerModule?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await db.ServerModules.FindAsync([id], ct).ConfigureAwait(false);
    }

    public async Task<ServerModule> AddAsync(ServerModule module, CancellationToken ct = default)
    {
        db.ServerModules.Add(module);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return module;
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(ServerModule module, CancellationToken ct = default)
    {
        db.ServerModules.Remove(module);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
