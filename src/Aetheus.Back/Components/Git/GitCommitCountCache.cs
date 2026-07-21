// SPDX-License-Identifier: EUPL-1.2
using Microsoft.Extensions.Caching.Memory;

namespace Aetheus.Back.Components.Git;

/// <summary>Short-lived per-repo cache for the total commit count. The count is an expensive full
/// rev-list/grep walk yet stable across pages and slow-changing, so it is cached briefly per
/// repo+ref+search; the page items stay live, so at worst the displayed total lags &lt;=60s after a
/// push (acceptable for a display total - avoids re-walking history per page).</summary>
internal static class GitCommitCountCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    public static async Task<int> GetAsync(
        IMemoryCache cache, IGitLightCliService cli,
        int repoId, string diskPath, string? effectiveRef, string? search, CancellationToken ct)
    {
        var key = $"git:commit-count:{repoId}:{effectiveRef}:{search}";
        if (cache.TryGetValue(key, out int cached)) return cached;
        var count = await cli.GetCommitCountAsync(diskPath, effectiveRef, search, ct).ConfigureAwait(false);
        cache.Set(key, count, Ttl);
        return count;
    }
}
