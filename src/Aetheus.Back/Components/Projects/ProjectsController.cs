// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Projects;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public class ProjectsController(IProjectService projectService, IResourceAuthorizationService authz) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PaginatedResult<ProjectDto>>> GetProjects([FromQuery] ProjectPaginationRequest request, CancellationToken ct)
    {
        var accessibleIds = await authz.GetAccessibleResourceIdsAsync(User, ResourceType.Project, Permission.Read, ct);
        if (accessibleIds is { Count: 0 }) return Ok(new PaginatedResult<ProjectDto>());
        return Ok(await projectService.GetProjectsAsync(request, accessibleIds, ct));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProjectDetailDto>> GetProject(int id, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, id, Permission.Read, ct))
            return Forbid();

        var project = await projectService.GetProjectDetailAsync(id, ct);
        if (project is null) return NotFound();
        return Ok(project);
    }

    [HttpPost]
    public async Task<ActionResult<ProjectDto>> CreateProject([FromBody] CreateProjectRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, null, Permission.Write, ct))
            return Forbid();

        var project = await projectService.CreateProjectAsync(request, ct);
        return CreatedAtAction(nameof(GetProject), new { id = project.Id }, project);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ProjectDto>> UpdateProject(int id, [FromBody] UpdateProjectRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, id, Permission.Write, ct))
            return Forbid();

        var project = await projectService.UpdateProjectAsync(id, request, ct);
        if (project is null) return NotFound();
        return Ok(project);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteProject(int id, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, id, Permission.Admin, ct))
            return Forbid();

        var deleted = await projectService.DeleteProjectAsync(id, ct);
        if (!deleted) return NotFound();
        return NoContent();
    }

    [HttpGet("{id:int}/servers")]
    public async Task<ActionResult<PaginatedResult<ProjectServerDto>>> GetProjectServers(
        int id, [FromQuery] PaginationRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, id, Permission.Read, ct))
            return Forbid();

        return Ok(await projectService.GetProjectServersPageAsync(id, request, ct));
    }

    [HttpPost("{id:int}/servers")]
    public async Task<ActionResult<ProjectServerDto>> CreateProjectServer(int id, [FromBody] CreateProjectServerRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, id, Permission.Write, ct))
            return Forbid();

        var result = await projectService.CreateProjectServerAsync(id, request, ct);
        return Ok(result);
    }

    [HttpPut("{id:int}/servers/{projectServerId:int}")]
    public async Task<ActionResult<ProjectServerDto>> UpdateProjectServer(int id, int projectServerId, [FromBody] UpdateProjectServerRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, id, Permission.Write, ct))
            return Forbid();

        var result = await projectService.UpdateProjectServerAsync(id, projectServerId, request, ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    [HttpDelete("{id:int}/servers/{projectServerId:int}")]
    public async Task<IActionResult> DeleteProjectServer(int id, int projectServerId, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, id, Permission.Write, ct))
            return Forbid();

        var deleted = await projectService.DeleteProjectServerAsync(id, projectServerId, ct);
        if (!deleted) return NotFound();
        return NoContent();
    }

    [HttpGet("{id:int}/tasks")]
    public async Task<ActionResult<PaginatedResult<ServerTaskDto>>> GetProjectTasks(int id, [FromQuery] PaginationRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, id, Permission.Read, ct))
            return Forbid();

        return Ok(await projectService.GetProjectTasksAsync(id, request, ct));
    }

    [HttpGet("{id:int}/logs")]
    public async Task<ActionResult<PaginatedResult<TaskLogDto>>> GetProjectLogs(int id, [FromQuery] PaginationRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, id, Permission.Read, ct))
            return Forbid();

        return Ok(await projectService.GetProjectLogsAsync(id, request, ct));
    }

    [HttpGet("{id:int}/activity")]
    public async Task<ActionResult<List<ProjectActivityDto>>> GetProjectActivity(int id, [FromQuery] int count = 20, CancellationToken ct = default)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, id, Permission.Read, ct))
            return Forbid();

        return Ok(await projectService.GetProjectActivityAsync(id, Math.Clamp(count, 1, 100), ct));
    }
}
