// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.ExternalRepos;

/// <summary>
/// Attach / inspect / sync / detach a project's external (mirror-backed) Git source. The whole
/// feature is always on (recette R-295 removed its flag). Every action is org-scoped on the project.
/// </summary>
[ApiController]
[Route("api/external-repos")]
[Authorize]
public class ExternalReposController(IExternalRepoService service, IResourceAuthorizationService authz) : ControllerBase
{
    // Recette R-321: a project without an external repository is the normal case, not a missing resource:
    // 204 keeps it out of the request error log, where every 404 is a warning.
    [HttpGet("project/{projectId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<ExternalRepoDto>> GetForProject(int projectId, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, projectId, Permission.Read, ct))
            return Forbid();

        var result = await service.GetForProjectAsync(projectId, ct);
        return result is null ? NoContent() : Ok(result);
    }

    [HttpPost("attach")]
    public async Task<ActionResult<ExternalRepoDto>> Attach([FromBody] AttachExternalRepoRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, request.ProjectId, Permission.Write, ct))
            return Forbid();

        var result = await service.AttachAsync(request, ct);
        return Ok(result);
    }

    [HttpPost("project/{projectId:int}/sync")]
    public async Task<ActionResult<ExternalRepoDto>> SyncNow(int projectId, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, projectId, Permission.Write, ct))
            return Forbid();

        var result = await service.SyncNowAsync(projectId, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("project/{projectId:int}")]
    public async Task<IActionResult> Detach(int projectId, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, projectId, Permission.Write, ct))
            return Forbid();

        var ok = await service.DetachAsync(projectId, ct);
        return ok ? NoContent() : NotFound();
    }
}
