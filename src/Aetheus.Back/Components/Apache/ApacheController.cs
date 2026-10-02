// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Apache;

[ApiController]
[Route("api/servers/{serverId:int}/apache")]
[Authorize]
[ServiceFilter(typeof(ValidateServerExistsFilter))]
public class ApacheController(IApacheService apacheService, IResourceAuthorizationService authz) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApacheDataDto>> GetState(int serverId, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Read, ct))
            return Forbid();
        var state = await apacheService.GetStateAsync(serverId, ct);
        return Ok(state);
    }

    [HttpGet("modules")]
    public async Task<ActionResult<List<ApacheModuleDto>>> GetModules(int serverId, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Read, ct))
            return Forbid();
        var modules = await apacheService.GetModulesAsync(serverId, ct);
        return Ok(modules);
    }

    [HttpGet("vhosts")]
    public async Task<ActionResult<List<ApacheVirtualHostDto>>> GetVirtualHosts(int serverId, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Read, ct))
            return Forbid();
        var vhosts = await apacheService.GetVirtualHostsAsync(serverId, ct);
        return Ok(vhosts);
    }

    [HttpPost("action")]
    public async Task<IActionResult> ExecuteAction(int serverId, [FromBody] ApacheActionRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Write, ct))
            return Forbid();
        await apacheService.ExecuteActionAsync(serverId, request, ct);
        return Ok();
    }

    [HttpPost("logs")]
    public async Task<IActionResult> GetLogs(int serverId, [FromBody] ApacheLogRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Read, ct))
            return Forbid();
        await apacheService.GetLogsAsync(serverId, request, ct);
        return Ok();
    }

    [HttpGet("vhosts/{siteName}/config")]
    public async Task<IActionResult> GetVHostConfig(int serverId, string siteName, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Read, ct))
            return Forbid();
        await apacheService.GetVHostConfigAsync(serverId, siteName, ct);
        return Ok();
    }

    [HttpPut("vhosts/{siteName}/config")]
    public async Task<IActionResult> SaveVHostConfig(int serverId, string siteName, [FromBody] ApacheVHostSaveRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Write, ct))
            return Forbid();
        var sanitizedRequest = request with { SiteName = siteName };
        await apacheService.SaveVHostConfigAsync(serverId, sanitizedRequest, ct);
        return Ok();
    }

    [HttpGet("htaccess")]
    public async Task<IActionResult> GetHtaccess(
        int serverId,
        // The (?!\.\.$) rejects a ".." segment: without it '.' and '-' in the class let
        // "/var/www/../../etc" through, which is not what an absolute-path bound is for.
        [FromQuery, System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(500), System.ComponentModel.DataAnnotations.RegularExpression(@"^(/(?!\.\.(?:/|$))[a-zA-Z0-9._\-]+)+/?$")] string documentRoot,
        CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Read, ct))
            return Forbid();
        await apacheService.GetHtaccessAsync(serverId, documentRoot, ct);
        return Ok();
    }

    [HttpPut("htaccess")]
    public async Task<IActionResult> SaveHtaccess(int serverId, [FromBody] ApacheHtaccessSaveRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Write, ct))
            return Forbid();
        await apacheService.SaveHtaccessAsync(serverId, request, ct);
        return Ok();
    }
}
