// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.AgentUpdate;
using Aetheus.Back.Components.Auth;

namespace Aetheus.Back.Components.Servers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public class ServersController(
    IServerLifecycleService lifecycle,
    IServerRetirementService retirement,
    IServerHeartbeatService heartbeats,
    IServerServiceManagementService services,
    IServerAgentContactService agentContact,
    IServerDiagnosticService diagnostic,
    IAgentUpdateService agentUpdate,
    IResourceAuthorizationService authz,
    IAuthService authService,
    ILogger<ServersController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PaginatedResult<ServerDto>>> GetServers(
        [FromQuery] PaginationRequest request,
        [FromQuery] ServerType? type = null,
        [FromQuery] ServerStatus? status = null,
        [FromQuery] AgentCompatibilityStatus? compatibility = null,
        CancellationToken ct = default)
    {
        var accessibleIds = await authz.GetAccessibleResourceIdsAsync(User, ResourceType.Server, Permission.Read, ct);
        if (accessibleIds is { Count: 0 }) return Ok(new PaginatedResult<ServerDto>());
        return Ok(await lifecycle.GetServersAsync(
            request, type, status, compatibility, accessibleIds, ct));
    }

    /// <summary>Recette R-211: the OS, agent versions and tags the servers list's column filters offer.</summary>
    [HttpGet("filter-values")]
    public async Task<ActionResult<ServerFilterValuesDto>> GetServerFilterValues(CancellationToken ct)
    {
        var accessibleIds = await authz.GetAccessibleResourceIdsAsync(User, ResourceType.Server, Permission.Read, ct);
        if (accessibleIds is { Count: 0 }) return Ok(new ServerFilterValuesDto());
        return Ok(await lifecycle.GetServerFilterValuesAsync(accessibleIds, ct));
    }

    [HttpGet("agent-compatibility-summary")]
    public async Task<ActionResult<AgentCompatibilitySummaryDto>> GetAgentCompatibilitySummary(
        CancellationToken ct)
    {
        var accessibleIds = await authz.GetAccessibleResourceIdsAsync(
            User, ResourceType.Server, Permission.Read, ct);
        return Ok(await lifecycle.GetAgentCompatibilitySummaryAsync(accessibleIds, ct));
    }

    [HttpGet("names")]
    public async Task<ActionResult<List<string>>> GetServerNames(CancellationToken ct)
    {
        var accessibleIds = await authz.GetAccessibleResourceIdsAsync(User, ResourceType.Server, Permission.Read, ct);
        return Ok(await lifecycle.GetServerNamesAsync(accessibleIds, ct));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ServerDetailDto>> GetServer(int id, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, id, Permission.Read, ct))
            return Forbid();

        var server = await lifecycle.GetServerDetailAsync(id, ct);
        if (server is null) return NotFound();
        return Ok(server);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ServerDto>> UpdateServer(int id, [FromBody] UpdateServerRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, id, Permission.Write, ct))
            return Forbid();

        var server = await lifecycle.UpdateServerAsync(id, request, ct);
        if (server is null) return NotFound();
        return Ok(server);
    }

    // PLAN-004 R-11: DELETE retires the server. Its row and links stay, its agent tokens are revoked,
    // it leaves every list and dispatch, and reinstalling the agent on the same machine revives it.
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> RetireServer(int id, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, id, Permission.Admin, ct))
            return Forbid();

        var retired = await retirement.RetireServerAsync(id, User.Identity?.Name ?? "unknown", ct);
        if (!retired) return NotFound();
        return NoContent();
    }

    [HttpGet("retired")]
    public async Task<ActionResult<PaginatedResult<RetiredServerDto>>> GetRetiredServers(
        [FromQuery] PaginationRequest request, CancellationToken ct)
    {
        var accessibleIds = await authz.GetAccessibleResourceIdsAsync(User, ResourceType.Server, Permission.Read, ct);
        if (accessibleIds is { Count: 0 }) return Ok(new PaginatedResult<RetiredServerDto>());
        return Ok(await retirement.GetRetiredServersAsync(request, accessibleIds, ct));
    }

    // The former hard delete, only for a server already retired (409 otherwise). Irreversible: it
    // also deletes the server's backup policies, which could not outlive their target host.
    [HttpDelete("{id:int}/permanent")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PurgeServer(int id, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, id, Permission.Admin, ct))
            return Forbid();

        var purged = await retirement.PurgeServerAsync(id, User.Identity?.Name ?? "unknown", ct);
        if (!purged) return NotFound();
        return NoContent();
    }

    // Item #11: secure-by-default pipeline-runner opt-in. Server.Admin gated (same weight as
    // delete) - flipping it on exposes the server to pipeline shell execution.
    [HttpPost("{id:int}/pipeline-runner")]
    public async Task<ActionResult<ServerDto>> SetPipelineRunner(int id, [FromBody] UpdatePipelineRunnerRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, id, Permission.Admin, ct))
            return Forbid();

        var actor = User.Identity?.Name ?? "anonymous";
        var server = await lifecycle.SetPipelineRunnerEnabledAsync(id, request.Enabled, actor, ct);
        if (server is null) return NotFound();
        return Ok(server);
    }

    // Container-isolation policy: force this runner to accept only container-isolated steps.
    // Server.Admin gated - it changes the security posture of pipeline execution on the server.
    [HttpPost("{id:int}/container-isolation-policy")]
    public async Task<ActionResult<ServerDto>> SetContainerIsolationPolicy(int id, [FromBody] UpdateContainerIsolationPolicyRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, id, Permission.Admin, ct))
            return Forbid();

        var actor = User.Identity?.Name ?? "anonymous";
        var server = await lifecycle.SetContainerIsolationRequiredAsync(id, request.Required, actor, ct);
        if (server is null) return NotFound();
        return Ok(server);
    }

    // Item #3 - Agent self-update progress: the agent calls this endpoint at each phase
    // transition of its self-update operation. Identity is the agent's JWT (ServerId claim),
    // so the request only carries the payload. Backend fans out to the server hub group.
    [HttpPost("{id:int}/agent/progress")]
    [Authorize(Policy = "AgentToken")]
    public async Task<IActionResult> ReportAgentUpdateProgress(int id, [FromBody] AgentUpdateProgressReport report, CancellationToken ct)
    {
        var claim = User.FindFirst("ServerId")?.Value;
        if (claim is null || !int.TryParse(claim, out var agentServerId) || agentServerId != id)
            return Forbid();

        await agentUpdate.BroadcastProgressAsync(new AgentUpdateProgressDto
        {
            ServerId = id,
            Phase = report.Phase,
            Percent = report.Percent,
            Message = report.Message
        }, ct);
        return NoContent();
    }

    [HttpPost("{id:int}/heartbeat")]
    [Authorize(Policy = "AgentToken")]
    public async Task<ActionResult<ServerHeartbeatResponseDto>> Heartbeat(int id, [FromBody] ServerHeartbeatDto heartbeat, CancellationToken ct)
    {
        var claim = User.FindFirst("ServerId")?.Value;
        if (claim is null || !int.TryParse(claim, out var agentServerId) || agentServerId != id)
            return Forbid();

        await heartbeats.ProcessHeartbeatAsync(id, heartbeat, ct);

        // Roll the agent's token while it's authenticated and healthy so a
        // long-lived agent never hits the hard 365-day expiry wall. Best-effort:
        // a renewal failure must NEVER turn a healthy heartbeat into a 500 (that
        // would make agents believe they're failing and retry-storm).
        ServerHeartbeatResponseDto? renewal = null;
        try
        {
            renewal = await authService.MaybeRenewAgentTokenAsync(id, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Agent token auto-renewal failed for server {ServerId}", id);
        }
        return Ok(renewal ?? new ServerHeartbeatResponseDto());
    }

    /// <summary>
    /// Probes whether the server's agent is reachable, derived from heartbeat
    /// freshness (agent communication is poll-based - there is no synchronous
    /// server-&gt;agent channel). The caller (front-end dialog) owns the
    /// retry/timeout loop; each call is bounded and cheap.
    /// </summary>
    [HttpPost("{id:int}/contact-agent")]
    public async Task<ActionResult<ContactAgentResultDto>> ContactAgent(int id, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, id, Permission.Read, ct))
            return Forbid();

        var result = await agentContact.ContactAgentAsync(id, ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// One-shot diagnostic explaining why a server may be offline - combines
    /// heartbeat freshness, agent token validity, and version metadata into a
    /// single human-readable summary. Cheap (single DB roundtrip) so the UI
    /// can call it on demand from a "Why offline?" button.
    /// </summary>
    [HttpGet("{id:int}/diagnostic")]
    public async Task<ActionResult<ServerDiagnosticDto>> GetDiagnostic(int id, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, id, Permission.Read, ct))
            return Forbid();

        var result = await diagnostic.DiagnoseAsync(id, ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// Queues an agent self-update for one server. The agent picks the task up on its next poll,
    /// downloads the latest build from <c>/downloads</c> and hands off to a detached platform
    /// updater. Progress/result surface through the existing task-log / SignalR channel.
    /// Requires <see cref="Permission.Admin"/> - self-update swaps the agent binaries.
    /// </summary>
    [HttpPost("{id:int}/agent/update")]
    public async Task<ActionResult<AgentUpdateResponse>> UpdateAgent(int id, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, id, Permission.Admin, ct))
            return Forbid();

        return Ok(await agentUpdate.QueueUpdateAsync(id, User.Identity?.Name ?? "unknown", ct));
    }

    /// <summary>
    /// Queues an agent self-update for every server the caller can administer.
    /// </summary>
    [HttpPost("agent/update-all")]
    public async Task<ActionResult<AgentUpdateAllResponse>> UpdateAllAgents(CancellationToken ct)
    {
        var accessibleIds = await authz.GetAccessibleResourceIdsAsync(User, ResourceType.Server, Permission.Admin, ct);
        if (accessibleIds is { Count: 0 }) return Ok(new AgentUpdateAllResponse());

        return Ok(await agentUpdate.QueueUpdateAllAsync(
            accessibleIds,
            User.Identity?.Name ?? "unknown",
            ct));
    }

    [HttpGet("agent/update-all-preview")]
    public async Task<ActionResult<AgentUpdateAllPreviewDto>> PreviewUpdateAllAgents(CancellationToken ct)
    {
        var accessibleIds = await authz.GetAccessibleResourceIdsAsync(
            User, ResourceType.Server, Permission.Admin, ct);
        return Ok(await agentUpdate.PreviewUpdateAllAsync(accessibleIds, ct));
    }

    [HttpGet("{id:int}/projects")]
    public async Task<ActionResult<PaginatedResult<ProjectDto>>> GetServerProjects(
        int id, [FromQuery] PaginationRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, id, Permission.Read, ct))
            return Forbid();

        return Ok(await lifecycle.GetServerProjectsAsync(id, request, ct));
    }

    [HttpGet("{id:int}/pipelines")]
    public async Task<ActionResult<List<PipelineDto>>> GetServerPipelines(int id, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, id, Permission.Read, ct))
            return Forbid();

        return Ok(await lifecycle.GetServerPipelinesAsync(id, ct));
    }

    [HttpGet("{id:int}/variable-libraries")]
    public async Task<ActionResult<List<VariableLibraryDto>>> GetServerVariableLibraries(int id, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, id, Permission.Read, ct))
            return Forbid();

        return Ok(await lifecycle.GetServerVariableLibrariesAsync(id, ct));
    }

    [HttpGet("{id:int}/vaults")]
    public async Task<ActionResult<List<VaultDto>>> GetServerVaults(int id, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, id, Permission.Read, ct))
            return Forbid();

        return Ok(await lifecycle.GetServerVaultsAsync(id, ct));
    }

    [HttpPost("{id:int}/services/action")]
    public async Task<IActionResult> ExecuteServiceAction(int id, [FromBody] ServiceActionRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, id, Permission.Write, ct))
            return Forbid();

        var taskId = await services.ExecuteServiceActionAsync(id, request, ct);
        return Ok(new ServiceTaskResponse { TaskId = taskId });
    }

    [HttpPost("{id:int}/services/install")]
    public async Task<IActionResult> InstallService(int id, [FromBody] ServiceInstallRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, id, Permission.Write, ct))
            return Forbid();

        var taskId = await services.InstallServiceAsync(id, request.ServiceName, ct);
        return Ok(new ServiceTaskResponse { TaskId = taskId });
    }

    [HttpPost("{id:int}/services/uninstall")]
    public async Task<IActionResult> UninstallService(int id, [FromBody] ServiceInstallRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, id, Permission.Write, ct))
            return Forbid();

        var taskId = await services.UninstallServiceAsync(id, request.ServiceName, ct);
        return Ok(new ServiceTaskResponse { TaskId = taskId });
    }

    // ADR-024 4.1: read the server's last reported OS-patch status (pending counts + packages).
    [HttpGet("{id:int}/security-updates")]
    public async Task<ActionResult<ServerSecurityUpdatesDto>> GetSecurityUpdates(int id, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, id, Permission.Read, ct))
            return Forbid();

        return Ok(await services.GetSecurityUpdatesAsync(id, ct));
    }

    // ADR-024 4.1: enqueue a system package upgrade - dry-run preview or consented apply.
    [HttpPost("{id:int}/system/upgrade")]
    public async Task<IActionResult> UpgradeSystem(int id, [FromBody] SystemUpgradeRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, id, Permission.Write, ct))
            return Forbid();

        var taskId = await services.UpgradeSystemAsync(id, request.DryRun, ct);
        return Ok(new ServiceTaskResponse { TaskId = taskId });
    }

    // ADR-024 4.2: read the server's last reported firewall (ufw) state + rules.
    [HttpGet("{id:int}/firewall")]
    public async Task<ActionResult<ServerFirewallDto>> GetFirewall(int id, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, id, Permission.Read, ct))
            return Forbid();

        return Ok(await services.GetFirewallAsync(id, ct));
    }

    [HttpPost("{id:int}/firewall/allow")]
    public async Task<IActionResult> FirewallAllow(int id, [FromBody] FirewallRuleRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, id, Permission.Write, ct))
            return Forbid();
        return Ok(new ServiceTaskResponse { TaskId = await services.FirewallAllowAsync(id, request, ct) });
    }

    [HttpPost("{id:int}/firewall/deny")]
    public async Task<IActionResult> FirewallDeny(int id, [FromBody] FirewallRuleRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, id, Permission.Write, ct))
            return Forbid();
        return Ok(new ServiceTaskResponse { TaskId = await services.FirewallDenyAsync(id, request, ct) });
    }

    [HttpPost("{id:int}/firewall/delete")]
    public async Task<IActionResult> FirewallDeleteRule(int id, [FromBody] FirewallRuleRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, id, Permission.Write, ct))
            return Forbid();
        return Ok(new ServiceTaskResponse { TaskId = await services.FirewallDeleteRuleAsync(id, request, ct) });
    }

    [HttpPost("{id:int}/firewall/toggle")]
    public async Task<IActionResult> FirewallToggle(int id, [FromBody] FirewallToggleRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, id, Permission.Write, ct))
            return Forbid();
        return Ok(new ServiceTaskResponse { TaskId = await services.FirewallToggleAsync(id, request.Enabled, ct) });
    }

    [HttpPost("{id:int}/services/logs")]
    public async Task<ActionResult<ServiceLogsResponse>> RequestServiceLogs(int id, [FromBody] ServiceLogsRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, id, Permission.Read, ct))
            return Forbid();

        var taskId = await services.CreateServiceLogsTaskAsync(id, request.ServiceName, request.Lines, request.Follow, ct);
        return Ok(new ServiceLogsResponse { TaskId = taskId });
    }

    [HttpGet("{id:int}/tasks")]
    public async Task<ActionResult<PaginatedResult<ServerTaskDto>>> GetServerTasks(int id, [FromQuery] PaginationRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, id, Permission.Read, ct))
            return Forbid();

        return Ok(await lifecycle.GetServerTasksAsync(id, request, ct));
    }

    [HttpGet("{id:int}/logs")]
    public async Task<ActionResult<PaginatedResult<TaskLogDto>>> GetServerLogs(int id, [FromQuery] PaginationRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, id, Permission.Read, ct))
            return Forbid();

        return Ok(await lifecycle.GetServerLogsAsync(id, request, ct));
    }
}
