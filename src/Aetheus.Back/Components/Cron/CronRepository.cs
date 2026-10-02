// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Cron;

public class CronRepository(AppDbContext db) : ICronRepository
{
    public Task<bool> ServerExistsAsync(int serverId, CancellationToken ct = default) =>
        ServerTaskRepositoryOperations.ServerExistsAsync(db, serverId, ct);

    public Task AddTaskAsync(ServerTask task, CancellationToken ct = default) =>
        ServerTaskRepositoryOperations.AddTaskAsync(db, task, ct);
}
