// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Environments;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class EnvironmentsController(IEnvironmentService environmentService, IResourceAuthorizationService authz) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PaginatedResult<EnvironmentDto>>> GetEnvironments(
        [FromQuery] int? projectId, [FromQuery] PaginationRequest request, CancellationToken ct)
    {
        var accessibleIds = await authz.GetAccessibleResourceIdsAsync(User, ResourceType.Environment, Permission.Read, ct);
        if (accessibleIds is { Count: 0 }) return Ok(new PaginatedResult<EnvironmentDto>());
        return Ok(await environmentService.GetEnvironmentsAsync(projectId, request, accessibleIds, ct));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<EnvironmentDto>> GetEnvironment(int id, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Environment, id, Permission.Read, ct))
            return Forbid();
        var env = await environmentService.GetEnvironmentAsync(id, ct);
        if (env is null) return NotFound();
        return Ok(env);
    }

    [HttpPost]
    public async Task<ActionResult<EnvironmentDto>> CreateEnvironment(
        [FromBody] CreateEnvironmentRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Environment, null, Permission.Write, ct))
            return Forbid();
        if (request.SourceEnvironmentId is { } sourceEnvironmentId
            && !await authz.HasPermissionAsync(
                User, ResourceType.Environment, sourceEnvironmentId, Permission.Read, ct))
            return Forbid();
        if (!await authz.CanAccessAllAsync(User, ResourceType.Server, Permission.Write, request.ServerIds, ct))
            return Forbid();
        var env = await environmentService.CreateEnvironmentAsync(request, ct);
        return CreatedAtAction(nameof(GetEnvironment), new { id = env.Id }, env);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<EnvironmentDto>> UpdateEnvironment(
        int id, [FromBody] UpdateEnvironmentRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Environment, id, Permission.Write, ct))
            return Forbid();
        if (!await authz.CanAccessAllAsync(User, ResourceType.Server, Permission.Write, request.ServerIds, ct))
            return Forbid();
        var env = await environmentService.UpdateEnvironmentAsync(id, request, ct);
        if (env is null) return NotFound();
        return Ok(env);
    }

    [HttpPost("{id:int}/duplicate")]
    public async Task<ActionResult<EnvironmentDto>> DuplicateEnvironment(
        int id, [FromBody] DuplicateEnvironmentRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Environment, id, Permission.Read, ct))
            return Forbid();
        if (!await authz.HasPermissionAsync(User, ResourceType.Environment, null, Permission.Write, ct))
            return Forbid();
        var env = await environmentService.DuplicateEnvironmentAsync(id, request.TargetProjectId, ct);
        if (env is null) return NotFound();
        return CreatedAtAction(nameof(GetEnvironment), new { id = env.Id }, env);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteEnvironment(int id, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Environment, id, Permission.Admin, ct))
            return Forbid();
        var deleted = await environmentService.DeleteEnvironmentAsync(id, ct);
        if (!deleted) return NotFound();
        return NoContent();
    }

    [HttpPost("{id:int}/project-servers/{projectServerId:int}")]
    public async Task<IActionResult> LinkProjectServer(int id, int projectServerId, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Environment, id, Permission.Write, ct))
            return Forbid();
        var guard = await EnsureProjectServerAccessAsync(projectServerId, ct);
        if (guard is not null) return guard;
        var result = await environmentService.LinkProjectServerAsync(id, projectServerId, ct);
        if (!result) return NotFound();
        return NoContent();
    }

    [HttpDelete("{id:int}/project-servers/{projectServerId:int}")]
    public async Task<IActionResult> UnlinkProjectServer(int id, int projectServerId, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Environment, id, Permission.Write, ct))
            return Forbid();
        var guard = await EnsureProjectServerAccessAsync(projectServerId, ct);
        if (guard is not null) return guard;
        var result = await environmentService.UnlinkProjectServerAsync(id, projectServerId, ct);
        if (!result) return NotFound();
        return NoContent();
    }

    // Anti-IDOR: the Environment Write permission alone does not authorize referencing an
    // arbitrary ProjectServer. Verify the caller can access the ProjectServer's parent project
    // before (un)linking. Returns null when access is granted, else the error result.
    private async Task<IActionResult?> EnsureProjectServerAccessAsync(int projectServerId, CancellationToken ct)
    {
        var projectId = await environmentService.GetProjectServerProjectIdAsync(projectServerId, ct);
        if (projectId is null) return NotFound();
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, projectId.Value, Permission.Read, ct))
            return Forbid();
        return null;
    }
}
