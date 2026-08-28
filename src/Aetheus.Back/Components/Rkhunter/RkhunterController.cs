// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Rkhunter;

[ApiController]
[Route("api/servers/{serverId:int}/rkhunter")]
[Authorize]
[ServiceFilter(typeof(ValidateServerExistsFilter))]
public class RkhunterController(IRkhunterService rkhunterService, IResourceAuthorizationService authz) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<RkhunterDataDto>> GetState(int serverId, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Read, ct))
            return Forbid();
        var state = await rkhunterService.GetStateAsync(serverId, ct);
        return Ok(state);
    }

    [HttpPost("action")]
    public async Task<IActionResult> ExecuteAction(int serverId, [FromBody] RkhunterActionRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Write, ct))
            return Forbid();
        await rkhunterService.ExecuteActionAsync(serverId, request, ct);
        return Ok();
    }

    [HttpPost("setup")]
    public async Task<IActionResult> Setup(int serverId, [FromBody] RkhunterSetupRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Admin, ct))
            return Forbid();
        await rkhunterService.SetupAsync(serverId, request, ct);
        return Ok();
    }

    [HttpPost("logs")]
    public async Task<IActionResult> GetLogs(int serverId, [FromBody] RkhunterLogRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Read, ct))
            return Forbid();
        await rkhunterService.GetLogsAsync(serverId, request, ct);
        return Ok();
    }

    [HttpGet("warnings")]
    public async Task<ActionResult<List<RkhunterWarningDto>>> GetWarnings(int serverId, [FromQuery] bool includeArchived = false, CancellationToken ct = default)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Read, ct))
            return Forbid();
        var warnings = await rkhunterService.GetWarningsAsync(serverId, includeArchived, ct);
        return Ok(warnings);
    }

    [HttpGet("history")]
    public async Task<ActionResult<List<RkhunterScanResultDto>>> GetScanHistory(int serverId, [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Read, ct))
            return Forbid();
        var history = await rkhunterService.GetScanHistoryAsync(serverId, limit, ct);
        return Ok(history);
    }

    [HttpPut("schedule")]
    public async Task<IActionResult> SetSchedule(int serverId, [FromBody] RkhunterScheduleRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Admin, ct))
            return Forbid();
        await rkhunterService.SetScheduleAsync(serverId, request, ct);
        return Ok();
    }
}
