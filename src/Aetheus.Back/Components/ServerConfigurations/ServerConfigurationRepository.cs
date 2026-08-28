// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.ServerConfigurations;

public class ServerConfigurationRepository(AppDbContext db) : IServerConfigurationRepository
{
    public async Task<Server?> GetServerWithDockerAndServicesReadOnlyAsync(int id, CancellationToken ct = default)
    {
        return await db.Servers
            .AsNoTracking()
            .Include(s => s.Services)
            .Include(s => s.DockerContainers)
            .Include(s => s.DockerImages)
            .Include(s => s.DockerComposeStacks)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<Server?> GetServerWithDockerAndServicesAsync(int id, CancellationToken ct = default)
    {
        return await db.Servers
            .Include(s => s.DockerContainers)
            .Include(s => s.DockerImages)
            .Include(s => s.DockerComposeStacks)
            .Include(s => s.Services)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == id, ct)
            .ConfigureAwait(false);
    }

    public Task AddTaskAsync(ServerTask task)
    {
        db.Tasks.Add(task);
        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
