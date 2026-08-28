// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;

namespace Aetheus.Back.Services;

public interface IResourceAuthorizationService
{
    Task<bool> HasPermissionAsync(ClaimsPrincipal user, ResourceType resourceType, int? resourceId, Permission required, CancellationToken ct = default);

    /// <summary>
    /// F-EXEC-1b: permission check for a principal identified by username only (no
    /// <see cref="ClaimsPrincipal"/>). Used to authorize non-interactive pipeline runs
    /// (webhook / scheduler) against the pipeline owner. The Admin role carries explicit
    /// <see cref="Permission.Admin"/> rows for every <see cref="ResourceType"/> (seeded by
    /// DbInitializer), so admin owners resolve correctly without an IsInRole shortcut.
    /// </summary>
    Task<bool> HasPermissionAsync(string username, ResourceType resourceType, int? resourceId, Permission required, CancellationToken ct = default);

    Task<List<int>?> GetAccessibleResourceIdsAsync(ClaimsPrincipal user, ResourceType resourceType, Permission required, CancellationToken ct = default);

    /// <summary>
    /// Organization ids the user belongs to (empty when none / unauthenticated). Used by the hubs to
    /// subscribe non-admin members to the per-org aggregate groups so they receive realtime
    /// Created/Registered events for org-scoped resources that did not exist when they connected.
    /// </summary>
    Task<List<int>> GetUserOrganizationIdsAsync(ClaimsPrincipal user, CancellationToken ct = default);

    /// <summary>
    /// Removes the cached role-id list for the given user. Must be called whenever a user's roles
    /// change or their SecurityStamp is rotated, otherwise stale role assignments grant access for
    /// up to the cache TTL.
    /// </summary>
    void InvalidateRoleCache(string username);
}
