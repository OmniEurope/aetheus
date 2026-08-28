// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Services;

public interface IPermissionRepository
{
    Task<List<int>> GetRoleIdsForUserAsync(string username, CancellationToken ct = default);
    Task<bool> HasPermissionAsync(List<int> roleIds, ResourceType resourceType, int? resourceId, Permission required, CancellationToken ct = default);
    Task<List<int>?> GetAccessibleResourceIdsAsync(List<int> roleIds, ResourceType resourceType, Permission required, CancellationToken ct = default);

    /// <summary>
    /// Returns the ids of every organization the user is a direct member of. Used to scope
    /// resource visibility/authorization to org-owned data (Server, Project, Plugin).
    /// </summary>
    Task<List<int>> GetOrganizationIdsForUsernameAsync(string username, CancellationToken ct = default);

    /// <summary>
    /// Returns the ids of org-owned resources of the given <paramref name="resourceType"/>
    /// whose OrganizationId is in <paramref name="orgIds"/>. Returns an empty list for
    /// resource types that are not directly org-owned.
    /// </summary>
    Task<List<int>> GetResourceIdsByOrganizationsAsync(ResourceType resourceType, List<int> orgIds, CancellationToken ct = default);

    /// <summary>
    /// True when the given resource is directly org-owned and the user is a member of its
    /// owning organization.
    /// </summary>
    Task<bool> IsUserInResourceOrganizationAsync(string username, ResourceType resourceType, int resourceId, CancellationToken ct = default);
}
