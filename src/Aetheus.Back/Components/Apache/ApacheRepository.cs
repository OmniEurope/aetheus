// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Apache;

public class ApacheRepository(AppDbContext db) : IApacheRepository
{
    public async Task<ApacheState?> GetStateAsync(int serverId, CancellationToken ct = default)
    {
        return await db.ApacheStates
            .AsNoTracking()
            .Where(a => a.ServerId == serverId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<ApacheModule>> GetModulesAsync(int serverId, CancellationToken ct = default)
    {
        return await db.ApacheModules
            .AsNoTracking()
            .Where(m => m.ServerId == serverId)
            .OrderBy(m => m.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<ApacheVirtualHost>> GetVirtualHostsAsync(int serverId, CancellationToken ct = default)
    {
        return await db.ApacheVirtualHosts
            .AsNoTracking()
            .Where(v => v.ServerId == serverId)
            .OrderBy(v => v.ServerName)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public Task<bool> ServerExistsAsync(int serverId, CancellationToken ct = default) =>
        ServerTaskRepositoryOperations.ServerExistsAsync(db, serverId, ct);

    public Task AddTaskAsync(ServerTask task, CancellationToken ct = default) =>
        ServerTaskRepositoryOperations.AddTaskAsync(db, task, ct);
}
