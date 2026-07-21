// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Configuration;
using Aetheus.Shared.DTOs;
using Microsoft.Extensions.Caching.Memory;

namespace Aetheus.Back.Components.Docker;

public class CachedDockerService(IDockerService inner, IMemoryCache cache) : IDockerService
{
    private static readonly TimeSpan CacheDuration = BackendRuntimeDefaults.InfrastructureCacheDuration;

    public async Task<List<DockerContainerDto>> GetContainersAsync(int serverId, CancellationToken ct = default)
    {
        var key = $"docker:containers:{serverId}";
        return await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await inner.GetContainersAsync(serverId, ct).ConfigureAwait(false);
        }).ConfigureAwait(false) ?? [];
    }

    public async Task<List<DockerImageDto>> GetImagesAsync(int serverId, CancellationToken ct = default)
    {
        var key = $"docker:images:{serverId}";
        return await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await inner.GetImagesAsync(serverId, ct).ConfigureAwait(false);
        }).ConfigureAwait(false) ?? [];
    }

    public async Task<List<DockerComposeStackDto>> GetComposeStacksAsync(int serverId, CancellationToken ct = default)
    {
        var key = $"docker:compose:{serverId}";
        return await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await inner.GetComposeStacksAsync(serverId, ct).ConfigureAwait(false);
        }).ConfigureAwait(false) ?? [];
    }

    public async Task<List<DockerNetworkDto>> GetNetworksAsync(int serverId, CancellationToken ct = default)
    {
        var key = $"docker:networks:{serverId}";
        return await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await inner.GetNetworksAsync(serverId, ct).ConfigureAwait(false);
        }).ConfigureAwait(false) ?? [];
    }

    public async Task<List<DockerVolumeDto>> GetVolumesAsync(int serverId, CancellationToken ct = default)
    {
        var key = $"docker:volumes:{serverId}";
        return await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await inner.GetVolumesAsync(serverId, ct).ConfigureAwait(false);
        }).ConfigureAwait(false) ?? [];
    }

    public async Task ExecuteActionAsync(int serverId, DockerActionRequest request, CancellationToken ct = default)
    {
        InvalidateCache(serverId);
        await inner.ExecuteActionAsync(serverId, request, ct).ConfigureAwait(false);
    }

    public async Task<string> GetContainerLogsAsync(int serverId, DockerContainerLogsRequest request, CancellationToken ct = default) =>
        await inner.GetContainerLogsAsync(serverId, request, ct).ConfigureAwait(false);

    public async Task PullImageAsync(int serverId, DockerPullImageRequest request, CancellationToken ct = default)
    {
        InvalidateCache(serverId);
        await inner.PullImageAsync(serverId, request, ct).ConfigureAwait(false);
    }

    public async Task RemoveImageAsync(int serverId, string imageId, CancellationToken ct = default)
    {
        InvalidateCache(serverId);
        await inner.RemoveImageAsync(serverId, imageId, ct).ConfigureAwait(false);
    }

    public async Task ExecuteComposeActionAsync(int serverId, DockerComposeActionRequest request, CancellationToken ct = default)
    {
        InvalidateCache(serverId);
        await inner.ExecuteComposeActionAsync(serverId, request, ct).ConfigureAwait(false);
    }

    public async Task PruneAsync(int serverId, DockerPruneRequest request, CancellationToken ct = default)
    {
        InvalidateCache(serverId);
        await inner.PruneAsync(serverId, request, ct).ConfigureAwait(false);
    }

    public async Task UpdateResourceLimitsAsync(int serverId, DockerResourceLimitsRequest request, CancellationToken ct = default)
    {
        InvalidateCache(serverId);
        await inner.UpdateResourceLimitsAsync(serverId, request, ct).ConfigureAwait(false);
    }

    public async Task InspectContainerAsync(int serverId, string containerId, CancellationToken ct = default) =>
        await inner.InspectContainerAsync(serverId, containerId, ct).ConfigureAwait(false);

    public async Task GetComposeFileAsync(int serverId, string stackName, CancellationToken ct = default) =>
        await inner.GetComposeFileAsync(serverId, stackName, ct).ConfigureAwait(false);

    public async Task SaveComposeFileAsync(int serverId, DockerComposeFileSaveRequest request, CancellationToken ct = default)
    {
        InvalidateCache(serverId);
        await inner.SaveComposeFileAsync(serverId, request, ct).ConfigureAwait(false);
    }

    public async Task ExecuteShellCommandAsync(int serverId, DockerExecRequest request, CancellationToken ct = default)
    {
        InvalidateCache(serverId);
        await inner.ExecuteShellCommandAsync(serverId, request, ct).ConfigureAwait(false);
    }

    public async Task GetContainerEnvVarsAsync(int serverId, string containerId, CancellationToken ct = default) =>
        await inner.GetContainerEnvVarsAsync(serverId, containerId, ct).ConfigureAwait(false);

    public async Task ListContainerFilesAsync(int serverId, DockerBrowseRequest request, CancellationToken ct = default) =>
        await inner.ListContainerFilesAsync(serverId, request, ct).ConfigureAwait(false);

    public async Task BuildImageAsync(int serverId, DockerBuildRequest request, CancellationToken ct = default)
    {
        InvalidateCache(serverId);
        await inner.BuildImageAsync(serverId, request, ct).ConfigureAwait(false);
    }

    private void InvalidateCache(int serverId)
    {
        cache.Remove($"docker:containers:{serverId}");
        cache.Remove($"docker:images:{serverId}");
        cache.Remove($"docker:compose:{serverId}");
        cache.Remove($"docker:networks:{serverId}");
        cache.Remove($"docker:volumes:{serverId}");
    }
}
