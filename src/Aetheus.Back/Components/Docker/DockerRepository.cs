// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Docker;

public class DockerRepository(AppDbContext db) : IDockerRepository
{
    public async Task<List<DockerContainer>> GetContainersAsync(int serverId, CancellationToken ct = default)
    {
        return await db.DockerContainers
            .AsNoTracking()
            .Where(c => c.ServerId == serverId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<DockerImage>> GetImagesAsync(int serverId, CancellationToken ct = default)
    {
        return await db.DockerImages
            .AsNoTracking()
            .Where(i => i.ServerId == serverId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<DockerComposeStack>> GetComposeStacksAsync(int serverId, CancellationToken ct = default)
    {
        return await db.DockerComposeStacks
            .AsNoTracking()
            .Where(s => s.ServerId == serverId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<DockerNetwork>> GetNetworksAsync(int serverId, CancellationToken ct = default)
    {
        return await db.DockerNetworks
            .AsNoTracking()
            .Where(n => n.ServerId == serverId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<DockerVolume>> GetVolumesAsync(int serverId, CancellationToken ct = default)
    {
        return await db.DockerVolumes
            .AsNoTracking()
            .Where(v => v.ServerId == serverId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public Task<bool> ServerExistsAsync(int serverId, CancellationToken ct = default) =>
        ServerTaskRepositoryOperations.ServerExistsAsync(db, serverId, ct);

    public Task AddTaskAsync(ServerTask task, CancellationToken ct = default) =>
        ServerTaskRepositoryOperations.AddTaskAsync(db, task, ct);
}
