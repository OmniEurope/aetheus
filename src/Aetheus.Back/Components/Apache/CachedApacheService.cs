// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Configuration;
using Aetheus.Shared.DTOs;
using Microsoft.Extensions.Caching.Memory;

namespace Aetheus.Back.Components.Apache;

public class CachedApacheService(IApacheService inner, IMemoryCache cache) : IApacheService
{
    private static readonly TimeSpan CacheDuration = BackendRuntimeDefaults.InfrastructureCacheDuration;

    public async Task<ApacheDataDto> GetStateAsync(int serverId, CancellationToken ct = default)
    {
        var key = $"apache:state:{serverId}";
        return await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await inner.GetStateAsync(serverId, ct).ConfigureAwait(false);
        }).ConfigureAwait(false) ?? new ApacheDataDto();
    }

    public async Task<List<ApacheModuleDto>> GetModulesAsync(int serverId, CancellationToken ct = default)
    {
        var key = $"apache:modules:{serverId}";
        return await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await inner.GetModulesAsync(serverId, ct).ConfigureAwait(false);
        }).ConfigureAwait(false) ?? [];
    }

    public async Task<List<ApacheVirtualHostDto>> GetVirtualHostsAsync(int serverId, CancellationToken ct = default)
    {
        var key = $"apache:vhosts:{serverId}";
        return await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await inner.GetVirtualHostsAsync(serverId, ct).ConfigureAwait(false);
        }).ConfigureAwait(false) ?? [];
    }

    public async Task ExecuteActionAsync(int serverId, ApacheActionRequest request, CancellationToken ct = default)
    {
        InvalidateCache(serverId);
        await inner.ExecuteActionAsync(serverId, request, ct).ConfigureAwait(false);
    }

    public async Task GetLogsAsync(int serverId, ApacheLogRequest request, CancellationToken ct = default) =>
        await inner.GetLogsAsync(serverId, request, ct).ConfigureAwait(false);

    public async Task GetVHostConfigAsync(int serverId, string siteName, CancellationToken ct = default) =>
        await inner.GetVHostConfigAsync(serverId, siteName, ct).ConfigureAwait(false);

    public async Task SaveVHostConfigAsync(int serverId, ApacheVHostSaveRequest request, CancellationToken ct = default)
    {
        InvalidateCache(serverId);
        await inner.SaveVHostConfigAsync(serverId, request, ct).ConfigureAwait(false);
    }

    public async Task GetHtaccessAsync(int serverId, string documentRoot, CancellationToken ct = default) =>
        await inner.GetHtaccessAsync(serverId, documentRoot, ct).ConfigureAwait(false);

    public async Task SaveHtaccessAsync(int serverId, ApacheHtaccessSaveRequest request, CancellationToken ct = default)
    {
        InvalidateCache(serverId);
        await inner.SaveHtaccessAsync(serverId, request, ct).ConfigureAwait(false);
    }

    private void InvalidateCache(int serverId)
    {
        cache.Remove($"apache:state:{serverId}");
        cache.Remove($"apache:modules:{serverId}");
        cache.Remove($"apache:vhosts:{serverId}");
    }
}
