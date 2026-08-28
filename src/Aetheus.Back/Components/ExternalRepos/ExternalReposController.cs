// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.ExternalRepos;

/// <summary>
/// Attach / inspect / sync / detach a project's external (mirror-backed) Git source. The whole
/// surface 404s while <c>Features:ExternalRepos</c> is off. Every action is org-scoped on the project.
/// </summary>
[ApiController]
[Route("api/external-repos")]
[Authorize]
public class ExternalReposController(IExternalRepoService service, IResourceAuthorizationService authz) : ControllerBase
{
    [HttpGet("enabled")]
    public ActionResult<bool> IsFeatureEnabled() => Ok(service.IsEnabled);

    [HttpGet("project/{projectId:int}")]
    public async Task<ActionResult<ExternalRepoDto>> GetForProject(int projectId, CancellationToken ct)
    {
        if (!service.IsEnabled) return NotFound();
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, projectId, Permission.Read, ct))
            return Forbid();

        var result = await service.GetForProjectAsync(projectId, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("attach")]
    public async Task<ActionResult<ExternalRepoDto>> Attach([FromBody] AttachExternalRepoRequest request, CancellationToken ct)
    {
        if (!service.IsEnabled) return NotFound();
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, request.ProjectId, Permission.Write, ct))
            return Forbid();

        var result = await service.AttachAsync(request, ct);
        return Ok(result);
    }

    [HttpPost("project/{projectId:int}/sync")]
    public async Task<ActionResult<ExternalRepoDto>> SyncNow(int projectId, CancellationToken ct)
    {
        if (!service.IsEnabled) return NotFound();
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, projectId, Permission.Write, ct))
            return Forbid();

        var result = await service.SyncNowAsync(projectId, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("project/{projectId:int}")]
    public async Task<IActionResult> Detach(int projectId, CancellationToken ct)
    {
        if (!service.IsEnabled) return NotFound();
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, projectId, Permission.Write, ct))
            return Forbid();

        var ok = await service.DetachAsync(projectId, ct);
        return ok ? NoContent() : NotFound();
    }
}
