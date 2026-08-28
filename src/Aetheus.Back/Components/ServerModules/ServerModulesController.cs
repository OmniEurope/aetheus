// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.ServerModules;

[ApiController]
[Route("api/servers/{serverId:int}/modules")]
[Authorize]
public class ServerModulesController(IServerModuleService service, IResourceAuthorizationService authz) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ServerModuleDto>>> GetModules(int serverId, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Read, ct))
            return Forbid();

        return Ok(await service.GetByServerIdAsync(serverId, ct));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ServerModuleDto>> GetModule(int serverId, int id, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Read, ct))
            return Forbid();

        var module = await service.GetByIdAsync(serverId, id, ct);
        if (module is null) return NotFound();
        return Ok(module);
    }

    [HttpPost]
    public async Task<ActionResult<ServerModuleDto>> CreateModule(int serverId, [FromBody] CreateServerModuleRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Write, ct))
            return Forbid();

        var module = await service.CreateAsync(serverId, request, ct);
        return CreatedAtAction(nameof(GetModule), new { serverId, id = module.Id }, module);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ServerModuleDto>> UpdateModule(int serverId, int id, [FromBody] UpdateServerModuleRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Write, ct))
            return Forbid();

        var module = await service.UpdateAsync(serverId, id, request, ct);
        if (module is null) return NotFound();
        return Ok(module);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteModule(int serverId, int id, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Admin, ct))
            return Forbid();

        var deleted = await service.DeleteAsync(serverId, id, ct);
        if (!deleted) return NotFound();
        return NoContent();
    }
}
