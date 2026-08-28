// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Auth;

public interface IRoleService
{
    Task<PaginatedResult<RoleDto>> GetRolesAsync(PaginationRequest request, CancellationToken ct = default);
    Task<RoleDto?> GetRoleAsync(int id, CancellationToken ct = default);
    Task<RoleDto> CreateRoleAsync(CreateRoleRequest request, CancellationToken ct = default);
    Task<RoleDto?> UpdateRoleAsync(int id, UpdateRoleRequest request, CancellationToken ct = default);
    Task<bool> DeleteRoleAsync(int id, CancellationToken ct = default);
    Task<List<ResourcePermissionDto>> GetPermissionsForRoleAsync(int roleId, CancellationToken ct = default);
    Task SetPermissionsForRoleAsync(int roleId, SetResourcePermissionsRequest request, CancellationToken ct = default);
    Task<RoleDto?> CloneRoleAsync(int id, CancellationToken ct = default);
    Task<UserPermissionSummaryDto?> GetEffectivePermissionsAsync(int userId, CancellationToken ct = default);
    Task<UserPermissionSummaryDto?> GetMyPermissionsAsync(string username, CancellationToken ct = default);

    Task<List<RoleUserDto>> GetUsersInRoleAsync(int roleId, CancellationToken ct = default);
    Task<PaginatedResult<RoleUserDto>> GetUsersInRoleAsync(
        int roleId, PaginationRequest request, CancellationToken ct = default);
    Task<PaginatedResult<RoleUserDto>> GetUsersAvailableForRoleAsync(
        int roleId, PaginationRequest request, CancellationToken ct = default);
    Task AddUserToRoleAsync(int roleId, int userId, CancellationToken ct = default);
    Task RemoveUserFromRoleAsync(int roleId, int userId, CancellationToken ct = default);
}
