// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Caching.Memory;

namespace Aetheus.Back.Services;

public sealed class ResourceAuthorizationService(IPermissionRepository permissionRepo, IMemoryCache cache, AuthzCacheEvictor evictor) : IResourceAuthorizationService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    // Every authz cache entry for a user is tied to the user's eviction token (held by the SINGLETON
    // AuthzCacheEvictor so it is shared across request scopes), so a single InvalidateRoleCache(username)
    // evicts ALL of them at once - crucially including the per-resource
    // authz:orgmember:{user}:{type}:{id} entries that a plain cache.Remove(key) can't reach (those keys
    // are unknown at invalidation time). Without this, a user removed from an organization kept their
    // org-scoped Read access until the 5-minute TTL expired.

    public async Task<bool> HasPermissionAsync(ClaimsPrincipal user, ResourceType resourceType, int? resourceId, Permission required, CancellationToken ct = default)
    {
        // Fast path: the Admin role is asserted as a JWT claim, so no DB round-trip needed.
        if (user.IsInRole("Admin"))
            return true;

        var username = user.Identity?.Name;
        return !string.IsNullOrEmpty(username)
            && await HasPermissionAsync(username, resourceType, resourceId, required, ct).ConfigureAwait(false);
    }

    public async Task<bool> HasPermissionAsync(string username, ResourceType resourceType, int? resourceId, Permission required, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(username))
            return false;

        if (resourceId.HasValue && IsOrgScoped(resourceType))
        {
            var orgKey = $"authz:orgmember:{username}:{resourceType}:{resourceId.Value}";
            if (!cache.TryGetValue(orgKey, out bool isMember))
            {
                isMember = await permissionRepo.IsUserInResourceOrganizationAsync(username, resourceType, resourceId.Value, ct).ConfigureAwait(false);
                cache.Set(orgKey, isMember, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheDuration }
                    .AddExpirationToken(evictor.TokenFor(username)));
            }
            if (isMember && required == Permission.Read)
                return true;
            if (!isMember && resourceType == ResourceType.PipelineTemplate)
                return false;
        }

        var roleIds = await GetCachedRoleIdsAsync(username, ct).ConfigureAwait(false);

        if (roleIds.Count == 0)
            return false;

        return await permissionRepo.HasPermissionAsync(roleIds, resourceType, resourceId, required, ct).ConfigureAwait(false);
    }

    public async Task<List<int>?> GetAccessibleResourceIdsAsync(ClaimsPrincipal user, ResourceType resourceType, Permission required, CancellationToken ct = default)
    {
        if (user.IsInRole("Admin"))
            return null;

        var username = user.Identity?.Name;
        if (string.IsNullOrEmpty(username))
            return [];

        var roleIds = await GetCachedRoleIdsAsync(username, ct).ConfigureAwait(false);

        var rolePermitted = roleIds.Count == 0
            ? []
            : await permissionRepo.GetAccessibleResourceIdsAsync(roleIds, resourceType, required, ct).ConfigureAwait(false);

        // Template catalog access is always bounded by organization membership for non-admins.
        // Membership grants Read on every template in the organization; Write still requires the
        // corresponding role permission and can never escape the member organizations.
        if (resourceType == ResourceType.PipelineTemplate)
        {
            var templateOrgIds = await GetCachedOrganizationIdsAsync(username, ct).ConfigureAwait(false);
            var organizationScoped = templateOrgIds.Count == 0
                ? []
                : await permissionRepo.GetResourceIdsByOrganizationsAsync(
                    resourceType, templateOrgIds, ct).ConfigureAwait(false);
            if (required == Permission.Read || rolePermitted is null) return organizationScoped;
            return rolePermitted.Intersect(organizationScoped).ToList();
        }

        // null = role grants wildcard access; nothing more to merge.
        if (rolePermitted is null) return null;

        if (!IsOrgScoped(resourceType)) return rolePermitted;

        var orgIds = await GetCachedOrganizationIdsAsync(username, ct).ConfigureAwait(false);
        if (orgIds.Count == 0) return rolePermitted;

        var orgScoped = await permissionRepo.GetResourceIdsByOrganizationsAsync(resourceType, orgIds, ct).ConfigureAwait(false);
        if (orgScoped.Count == 0) return rolePermitted;

        return rolePermitted.Union(orgScoped).ToList();
    }

    public async Task<List<int>> GetUserOrganizationIdsAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        var username = user.Identity?.Name;
        return string.IsNullOrEmpty(username)
            ? []
            : await GetCachedOrganizationIdsAsync(username, ct).ConfigureAwait(false);
    }

    private static bool IsOrgScoped(ResourceType type) =>
        type is ResourceType.Server or ResourceType.Project or ResourceType.PipelineTemplate;

    private async Task<List<int>> GetCachedOrganizationIdsAsync(string username, CancellationToken ct)
    {
        var key = $"authz:orgs:{username}";
        return await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            entry.AddExpirationToken(evictor.TokenFor(username));
            return await permissionRepo.GetOrganizationIdsForUsernameAsync(username, ct).ConfigureAwait(false);
        }).ConfigureAwait(false) ?? [];
    }

    private async Task<List<int>> GetCachedRoleIdsAsync(string username, CancellationToken ct)
    {
        var key = $"authz:roles:{username}";
        return await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            entry.AddExpirationToken(evictor.TokenFor(username));
            return await permissionRepo.GetRoleIdsForUserAsync(username, ct).ConfigureAwait(false);
        }).ConfigureAwait(false) ?? [];
    }

    public void InvalidateRoleCache(string username)
    {
        if (string.IsNullOrEmpty(username)) return;

        // Cancelling the user's eviction token (via the shared singleton evictor) drops every entry
        // tied to it (roles, orgs, and the per-resource orgmember entries) across ALL request scopes,
        // then a fresh token is handed out for subsequent reads.
        evictor.Evict(username);

        // Belt-and-suspenders for the two named keys (already covered by the token above).
        cache.Remove($"authz:roles:{username}");
        cache.Remove($"authz:orgs:{username}");
    }
}
