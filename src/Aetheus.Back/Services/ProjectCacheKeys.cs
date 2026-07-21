// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Services;

/// <summary>
/// Well-known <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/> keys for project-scoped
/// caches. Shared across modules so the read-path producer (ProjectService) and the invalidators on
/// the write path (git push, external-repo mirror sync) reference the exact same key and can never
/// drift (G7LM: the per-id "last git update" cache was previously never purged).
/// </summary>
internal static class ProjectCacheKeys
{
    /// <summary>Per-project "last git update" timestamp (TTL ~10 min).</summary>
    public static string GitUpdate(int projectId) => $"projects:gitupdate:{projectId}";
}
