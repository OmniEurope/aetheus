// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Components.Docker;

public interface IDockerService
{
    Task<List<DockerContainerDto>> GetContainersAsync(int serverId, CancellationToken ct = default);
    Task ExecuteActionAsync(int serverId, DockerActionRequest request, CancellationToken ct = default);
    Task<string> GetContainerLogsAsync(int serverId, DockerContainerLogsRequest request, CancellationToken ct = default);
    Task<List<DockerImageDto>> GetImagesAsync(int serverId, CancellationToken ct = default);
    Task PullImageAsync(int serverId, DockerPullImageRequest request, CancellationToken ct = default);
    Task RemoveImageAsync(int serverId, string imageId, CancellationToken ct = default);
    Task<List<DockerComposeStackDto>> GetComposeStacksAsync(int serverId, CancellationToken ct = default);
    Task ExecuteComposeActionAsync(int serverId, DockerComposeActionRequest request, CancellationToken ct = default);
    Task<List<DockerNetworkDto>> GetNetworksAsync(int serverId, CancellationToken ct = default);
    Task<List<DockerVolumeDto>> GetVolumesAsync(int serverId, CancellationToken ct = default);
    Task PruneAsync(int serverId, DockerPruneRequest request, CancellationToken ct = default);
    Task UpdateResourceLimitsAsync(int serverId, DockerResourceLimitsRequest request, CancellationToken ct = default);
    Task InspectContainerAsync(int serverId, string containerId, CancellationToken ct = default);
    Task GetComposeFileAsync(int serverId, string stackName, CancellationToken ct = default);
    Task SaveComposeFileAsync(int serverId, DockerComposeFileSaveRequest request, CancellationToken ct = default);
    Task ExecuteShellCommandAsync(int serverId, DockerExecRequest request, CancellationToken ct = default);
    Task GetContainerEnvVarsAsync(int serverId, string containerId, CancellationToken ct = default);
    Task ListContainerFilesAsync(int serverId, DockerBrowseRequest request, CancellationToken ct = default);
    Task BuildImageAsync(int serverId, DockerBuildRequest request, CancellationToken ct = default);
}
