// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aetheus.Back.Components.Auth;

[ApiController]
[Route("api/roles")]
[Authorize]
public class RolesController(IRoleService roleService) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<PaginatedResult<RoleDto>>> GetRoles(
        [FromQuery] PaginationRequest request, CancellationToken ct)
    {
        return Ok(await roleService.GetRolesAsync(request, ct));
    }

    [HttpGet("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<RoleDto>> GetRole(int id, CancellationToken ct)
    {
        var role = await roleService.GetRoleAsync(id, ct);
        if (role is null) return NotFound();
        return Ok(role);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<RoleDto>> CreateRole([FromBody] CreateRoleRequest request, CancellationToken ct)
    {
        var role = await roleService.CreateRoleAsync(request, ct);
        return CreatedAtAction(nameof(GetRole), new { id = role.Id }, role);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<RoleDto>> UpdateRole(int id, [FromBody] UpdateRoleRequest request, CancellationToken ct)
    {
        var role = await roleService.UpdateRoleAsync(id, request, ct);
        if (role is null) return NotFound();
        return Ok(role);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteRole(int id, CancellationToken ct)
    {
        var deleted = await roleService.DeleteRoleAsync(id, ct);
        if (!deleted) return NotFound();
        return NoContent();
    }

    [HttpGet("{id:int}/permissions")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<List<ResourcePermissionDto>>> GetPermissions(int id, CancellationToken ct)
    {
        return Ok(await roleService.GetPermissionsForRoleAsync(id, ct));
    }

    [HttpPut("{id:int}/permissions")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SetPermissions(int id, [FromBody] SetResourcePermissionsRequest request, CancellationToken ct)
    {
        await roleService.SetPermissionsForRoleAsync(id, request, ct);
        return NoContent();
    }

    [HttpPost("{id:int}/clone")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<RoleDto>> CloneRole(int id, CancellationToken ct)
    {
        var role = await roleService.CloneRoleAsync(id, ct);
        if (role is null) return NotFound();
        return CreatedAtAction(nameof(GetRole), new { id = role.Id }, role);
    }

    [HttpGet("{id:int}/users")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<PaginatedResult<RoleUserDto>>> GetUsersInRole(
        int id, [FromQuery] PaginationRequest request, CancellationToken ct)
    {
        return Ok(await roleService.GetUsersInRoleAsync(id, request, ct));
    }

    [HttpGet("{id:int}/available-users")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<PaginatedResult<RoleUserDto>>> GetUsersAvailableForRole(
        int id, [FromQuery] PaginationRequest request, CancellationToken ct)
    {
        return Ok(await roleService.GetUsersAvailableForRoleAsync(id, request, ct));
    }

    [HttpPost("{id:int}/users")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AddUserToRole(int id, [FromBody] AddUserToRoleRequest request, CancellationToken ct)
    {
        await roleService.AddUserToRoleAsync(id, request.UserId, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}/users/{userId:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RemoveUserFromRole(int id, int userId, CancellationToken ct)
    {
        await roleService.RemoveUserFromRoleAsync(id, userId, ct);
        return NoContent();
    }

    [HttpGet("~/api/users/{userId:int}/effective-permissions")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<UserPermissionSummaryDto>> GetEffectivePermissions(int userId, CancellationToken ct)
    {
        var summary = await roleService.GetEffectivePermissionsAsync(userId, ct);
        if (summary is null) return NotFound();
        return Ok(summary);
    }

    // Any authenticated user reads their OWN effective permissions (drives permission-gated UI).
    // Only [Authorize] here - must NOT inherit an Admin role requirement, or every non-admin
    // strands their permission-gated buttons as disabled. See UsersController for the same pattern.
    [HttpGet("~/api/users/me/permissions")]
    public async Task<ActionResult<UserPermissionSummaryDto>> GetMyPermissions(CancellationToken ct)
    {
        var username = User.Identity?.Name;
        if (string.IsNullOrEmpty(username)) return Unauthorized();
        var summary = await roleService.GetMyPermissionsAsync(username, ct);
        if (summary is null) return NotFound();
        return Ok(summary);
    }
}
