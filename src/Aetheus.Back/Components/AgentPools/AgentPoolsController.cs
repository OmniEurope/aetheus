// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.AgentPools;

[ApiController]
[Route("api/agent-pools")]
[Authorize]
public class AgentPoolsController(IAgentPoolService poolService, IResourceAuthorizationService authz) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PaginatedResult<AgentPoolDto>>> GetPools(
        [FromQuery] PaginationRequest request, CancellationToken ct)
    {
        var accessibleIds = await authz.GetAccessibleResourceIdsAsync(User, ResourceType.AgentPool, Permission.Read, ct);
        if (accessibleIds is { Count: 0 }) return Ok(new PaginatedResult<AgentPoolDto>());
        return Ok(await poolService.GetPoolsAsync(request, accessibleIds, ct));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<AgentPoolDto>> GetPool(int id, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.AgentPool, id, Permission.Read, ct))
            return Forbid();
        var pool = await poolService.GetPoolAsync(id, ct);
        if (pool is null) return NotFound();
        return Ok(pool);
    }

    [HttpPost]
    public async Task<ActionResult<AgentPoolDto>> CreatePool(
        [FromBody] CreateAgentPoolRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.AgentPool, null, Permission.Write, ct))
            return Forbid();
        if (!await authz.CanAccessAllAsync(User, ResourceType.Server, Permission.Write, request.ServerIds, ct))
            return Forbid();
        var pool = await poolService.CreatePoolAsync(request, ct);
        return CreatedAtAction(nameof(GetPool), new { id = pool.Id }, pool);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<AgentPoolDto>> UpdatePool(
        int id, [FromBody] UpdateAgentPoolRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.AgentPool, id, Permission.Write, ct))
            return Forbid();
        if (!await authz.CanAccessAllAsync(User, ResourceType.Server, Permission.Write, request.ServerIds, ct))
            return Forbid();
        var pool = await poolService.UpdatePoolAsync(id, request, ct);
        if (pool is null) return NotFound();
        return Ok(pool);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeletePool(int id, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.AgentPool, id, Permission.Admin, ct))
            return Forbid();
        var deleted = await poolService.DeletePoolAsync(id, ct);
        if (!deleted) return NotFound();
        return NoContent();
    }
}
