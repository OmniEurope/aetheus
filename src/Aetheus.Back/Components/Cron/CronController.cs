// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Shared;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aetheus.Back.Components.Cron;

[ApiController]
[Route("api/servers/{serverId:int}/cron")]
[Authorize]
[ServiceFilter(typeof(ValidateServerExistsFilter))]
public class CronController(ICronService cronService, IResourceAuthorizationService authz) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> SaveJob(int serverId, [FromBody] CronJobSaveRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Write, ct))
            return Forbid();
        await cronService.SaveJobAsync(serverId, request, ct);
        return Ok();
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteJob(int serverId, [FromBody] CronJobDeleteRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Write, ct))
            return Forbid();
        await cronService.DeleteJobAsync(serverId, request, ct);
        return Ok();
    }
}
