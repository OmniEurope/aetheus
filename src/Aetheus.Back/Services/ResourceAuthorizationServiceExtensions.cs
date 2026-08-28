// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;

namespace Aetheus.Back.Services;

internal static class ResourceAuthorizationServiceExtensions
{
    public static async Task<bool> CanAccessAllAsync(
        this IResourceAuthorizationService authorization,
        ClaimsPrincipal user,
        ResourceType resourceType,
        Permission permission,
        IEnumerable<int> resourceIds,
        CancellationToken ct)
    {
        var requestedIds = resourceIds.Distinct().ToList();
        if (requestedIds.Count == 0) return true;

        var accessibleIds = await authorization
            .GetAccessibleResourceIdsAsync(user, resourceType, permission, ct)
            .ConfigureAwait(false);
        return accessibleIds is null || requestedIds.All(accessibleIds.Contains);
    }
}
