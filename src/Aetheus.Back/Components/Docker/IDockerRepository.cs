// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Docker;

public interface IDockerRepository
{
    Task<List<DockerContainer>> GetContainersAsync(int serverId, CancellationToken ct = default);
    Task<List<DockerImage>> GetImagesAsync(int serverId, CancellationToken ct = default);
    Task<List<DockerComposeStack>> GetComposeStacksAsync(int serverId, CancellationToken ct = default);
    Task<List<DockerNetwork>> GetNetworksAsync(int serverId, CancellationToken ct = default);
    Task<List<DockerVolume>> GetVolumesAsync(int serverId, CancellationToken ct = default);
    Task<bool> ServerExistsAsync(int serverId, CancellationToken ct = default);
    Task AddTaskAsync(ServerTask task, CancellationToken ct = default);
}
