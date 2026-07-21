// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Tasks;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aetheus.Back.Components.Logs;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class LogsController(
    ILogService logService,
    ITaskService taskService,
    IResourceAuthorizationService authz,
    IAuditService audit) : ControllerBase
{
    [HttpGet("task/{taskId:int}")]
    public async Task<ActionResult<List<TaskLogDto>>> GetTaskLogs(int taskId, [FromQuery] int? maxLines, CancellationToken ct)
    {
        var serverId = await taskService.GetTaskServerIdAsync(taskId, ct);
        if (serverId is null) return NotFound();
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId.Value, Permission.Read, ct))
            return Forbid();

        return Ok(await logService.GetTaskLogsAsync(taskId, maxLines, ct));
    }

    [HttpGet("task/{taskId:int}/unmasked")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<List<TaskLogDto>>> GetTaskLogsUnmasked(int taskId, [FromQuery] int? maxLines, CancellationToken ct)
    {
        var serverId = await taskService.GetTaskServerIdAsync(taskId, ct);
        if (serverId is null) return NotFound();
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId.Value, Permission.Admin, ct))
            return Forbid();

        // Dedicated audit trail for high-sensitivity unmasked-secret access.
        await audit.LogAsync("AccessedUnmasked", "TaskLogs", taskId, $"Server={serverId.Value}", ct);

        return Ok(await logService.GetTaskLogsUnmaskedAsync(taskId, maxLines, ct));
    }

    [HttpPost]
    [Authorize(Policy = "AgentToken")]
    public async Task<IActionResult> AppendLog([FromBody] AppendLogRequest request, CancellationToken ct)
    {
        if (!await IsAgentAuthorizedForTaskAsync(request.TaskId, ct))
            return Forbid();

        await logService.AppendLogAsync(request, ct);
        return Ok();
    }

    [HttpPost("batch")]
    [Authorize(Policy = "AgentToken")]
    public async Task<IActionResult> AppendLogs([FromBody] List<AppendLogRequest> requests, CancellationToken ct)
    {
        if (requests.Count == 0)
            return Ok();

        // Cap batch size to prevent DoS via huge agent payloads.
        if (requests.Count > 500)
            return BadRequest(new ApiError { Message = "Batch size exceeds maximum of 500 entries." });

        var claim = User.FindFirst("ServerId")?.Value;
        if (claim is null || !int.TryParse(claim, out var agentServerId))
            return Forbid();

        // Hardening (#47): single batched lookup instead of N+1 per-line auth queries.
        // Use a HashSet directly to avoid the LINQ Distinct() + ToList() double allocation
        // on every batch.
        var distinctIds = new HashSet<int>(requests.Count);
        foreach (var r in requests) distinctIds.Add(r.TaskId);
        var taskServerIds = await taskService.GetServerIdsForTasksAsync(distinctIds, ct);
        foreach (var id in distinctIds)
        {
            if (!taskServerIds.TryGetValue(id, out var sid) || sid != agentServerId)
                return Forbid();
        }

        await logService.AppendLogsAsync(requests, ct);
        return Ok();
    }

    private async Task<bool> IsAgentAuthorizedForTaskAsync(int taskId, CancellationToken ct)
    {
        var claim = User.FindFirst("ServerId")?.Value;
        if (claim is null || !int.TryParse(claim, out var agentServerId))
            return false;

        var taskServerId = await taskService.GetTaskServerIdAsync(taskId, ct);
        return taskServerId.HasValue && taskServerId.Value == agentServerId;
    }
}
