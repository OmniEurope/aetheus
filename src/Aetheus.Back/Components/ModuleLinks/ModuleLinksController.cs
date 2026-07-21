// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aetheus.Back.Components.ModuleLinks;

[ApiController]
[Route("api/servers/{serverId:int}/module-links")]
[Authorize]
public class ModuleLinksController(IModuleLinkService service, IResourceAuthorizationService authz) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ModuleLinkDto>>> GetLinks(int serverId, CancellationToken ct)
    {
        // F-04: per-server Read on the owning server.
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Read, ct))
            return Forbid();
        var links = await service.GetLinksAsync(serverId, ct);
        return Ok(links);
    }

    [HttpGet("resource")]
    public async Task<ActionResult<List<LinkedResourceDto>>> GetLinksForResource(
        int serverId,
        [FromQuery] ModuleLinkType sourceType,
        [FromQuery] string sourceIdentifier,
        CancellationToken ct)
    {
        // F-04: per-server Read.
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Read, ct))
            return Forbid();
        var links = await service.GetLinksForResourceAsync(serverId, sourceType, sourceIdentifier, ct);
        return Ok(links);
    }

    [HttpPost("resource-page")]
    public async Task<ActionResult<PaginatedResult<LinkedResourceDto>>> GetLinksPage(
        int serverId, [FromBody] ModuleLinkPageRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Read, ct))
            return Forbid();
        var links = await service.GetLinksPageAsync(serverId, request, ct);
        return Ok(links);
    }

    [HttpPost]
    public async Task<ActionResult<ModuleLinkDto>> CreateLink(int serverId, [FromBody] CreateModuleLinkRequest request, CancellationToken ct)
    {
        // F-04: per-server Write.
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Write, ct))
            return Forbid();
        var link = await service.CreateLinkAsync(serverId, request, ct);
        return Ok(link);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteLink(int serverId, int id, CancellationToken ct)
    {
        // F-04: resolve the link's actual server (defends against id from another server)
        // and require Admin on it.
        var ownerServerId = await service.GetServerIdForLinkAsync(id, ct);
        if (ownerServerId is null) return NotFound();
        if (ownerServerId.Value != serverId) return NotFound();
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, ownerServerId.Value, Permission.Admin, ct))
            return Forbid();

        await service.DeleteLinkAsync(id, ct);
        return NoContent();
    }

    [HttpPost("auto-detect")]
    public async Task<IActionResult> AutoDetect(int serverId, CancellationToken ct)
    {
        // F-04: per-server Write.
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Write, ct))
            return Forbid();
        await service.AutoDetectLinksAsync(serverId, ct);
        return Ok();
    }
}
