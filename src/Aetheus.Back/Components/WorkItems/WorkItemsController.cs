// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aetheus.Back.Components.WorkItems;

[ApiController]
[Route("api/work-items")]
[Authorize]
public class WorkItemsController(IWorkItemService workItemService, IResourceAuthorizationService authz) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PaginatedResult<WorkItemDto>>> GetWorkItems(
        [FromQuery] WorkItemPaginationRequest request, CancellationToken ct)
    {
        // F-03: when ProjectId is missing, refuse - the previous behaviour returned items across
        // every project to any authenticated user. Force callers to scope to a project they can read.
        if (!request.ProjectId.HasValue)
            return BadRequest("projectId is required.");
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, request.ProjectId.Value, Permission.Read, ct))
            return Forbid();
        return Ok(await workItemService.GetWorkItemsAsync(request, ct));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<WorkItemDetailDto>> GetWorkItem(int id, CancellationToken ct)
    {
        // F-03: enforce per-project Read on the work item's owning project.
        var projectId = await workItemService.GetProjectIdForItemAsync(id, ct);
        if (projectId is null) return NotFound();
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, projectId.Value, Permission.Read, ct))
            return Forbid();

        var result = await workItemService.GetWorkItemDetailAsync(id, ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<WorkItemDto>> CreateWorkItem([FromBody] CreateWorkItemRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, request.ProjectId, Permission.Write, ct))
            return Forbid();
        var result = await workItemService.CreateWorkItemAsync(request, ct);
        return CreatedAtAction(nameof(GetWorkItem), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<WorkItemDto>> UpdateWorkItem(int id, [FromBody] UpdateWorkItemRequest request, CancellationToken ct)
    {
        // F-03: enforce per-project Write on the existing item's project (not from request body).
        var projectId = await workItemService.GetProjectIdForItemAsync(id, ct);
        if (projectId is null) return NotFound();
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, projectId.Value, Permission.Write, ct))
            return Forbid();

        var result = await workItemService.UpdateWorkItemAsync(id, request, ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    [HttpPatch("{id:int}/move")]
    public async Task<ActionResult<WorkItemDto>> MoveWorkItem(int id, [FromBody] MoveWorkItemRequest request, CancellationToken ct)
    {
        // Card drag on the board: Write on the item's project (resolved server-side, not from the body).
        var projectId = await workItemService.GetProjectIdForItemAsync(id, ct);
        if (projectId is null) return NotFound();
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, projectId.Value, Permission.Write, ct))
            return Forbid();

        var result = await workItemService.MoveWorkItemAsync(id, request, ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteWorkItem(int id, CancellationToken ct)
    {
        // F-03: enforce per-project Admin on delete.
        var projectId = await workItemService.GetProjectIdForItemAsync(id, ct);
        if (projectId is null) return NotFound();
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, projectId.Value, Permission.Admin, ct))
            return Forbid();

        var deleted = await workItemService.DeleteWorkItemAsync(id, ct);
        if (!deleted) return NotFound();
        return NoContent();
    }

    [HttpGet("board/{projectId:int}")]
    public async Task<ActionResult<List<WorkItemBoardColumn>>> GetBoard(int projectId, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, projectId, Permission.Read, ct))
            return Forbid();
        return Ok(await workItemService.GetBoardAsync(projectId, ct));
    }
}
