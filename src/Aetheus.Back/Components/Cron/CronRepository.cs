// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Cron;

public class CronRepository(AppDbContext db) : ICronRepository
{
    public async Task<bool> ServerExistsAsync(int serverId, CancellationToken ct = default)
    {
        return await db.Servers
            .AnyAsync(s => s.Id == serverId, ct)
            .ConfigureAwait(false);
    }

    public async Task AddTaskAsync(ServerTask task, CancellationToken ct = default)
    {
        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
