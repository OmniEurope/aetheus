// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aetheus.Back.Components.GitGraph;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GitGraphController(IGitGraphService service, IResourceAuthorizationService authz) : ControllerBase
{
    [HttpGet("commits/{id:int}")]
    public async Task<ActionResult<GitCommitDto>> GetCommit(int id, CancellationToken ct)
    {
        var commit = await service.GetCommitAsync(id, ct);
        if (commit is null) return NotFound();
        // Uniform 404 on no-access: the perm check needs the loaded entity's ProjectId, so we can't
        // gate before the lookup - but returning 403 here would leak existence ("exists but no
        // access" vs "does not exist"). Collapse both to 404.
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, commit.ProjectId, Permission.Read, ct))
            return NotFound();
        return Ok(commit);
    }

    // S-FEAT-29: project-scoped commit↔release↔artifact graph for the project git-graph view.
    [HttpGet("project/{projectId:int}")]
    public async Task<ActionResult<ProjectGitGraphDto>> GetProjectGraph(int projectId, CancellationToken ct)
    {
        // projectId is known up-front, so the access check can gate before the lookup.
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, projectId, Permission.Read, ct))
            return NotFound();
        return Ok(await service.GetProjectGraphAsync(projectId, ct));
    }

    [HttpGet("branches/{id:int}")]
    public async Task<ActionResult<GitBranchDto>> GetBranch(int id, CancellationToken ct)
    {
        var branch = await service.GetBranchAsync(id, ct);
        if (branch is null) return NotFound();
        // Uniform 404 on no-access (see GetCommit) - avoid leaking existence via a 403/404 split.
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, branch.ProjectId, Permission.Read, ct))
            return NotFound();
        return Ok(branch);
    }
}
