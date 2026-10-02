// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// What agents need to know about run workspaces on disk. It sits apart from
/// <see cref="PipelinesController"/> because that file is at its line budget, and because this serves
/// an agent's disk housekeeping rather than the pipeline API the UI talks to.
/// </summary>
[ApiController]
[Route("api/pipelines/runs")]
[Authorize]
public sealed class PipelineRunWorkspacesController(IPipelineRunService runService) : ControllerBase
{
    /// <summary>
    /// Workspace slots of the runs still in flight, so an agent can reclaim the disk of the ones that
    /// are not.
    ///
    /// Run workspaces are durable since they moved off /tmp, where systemd's PrivateTmp emptied them on
    /// every agent restart and took live runs down with them (production run 2185). Nothing reclaims
    /// them for free any more, hence this endpoint.
    ///
    /// No per-server check: the slots are opaque hashes carrying neither the run id nor the project, an
    /// agent keeping a workspace it does not own only wastes its own disk until the next sweep, and a
    /// wrong answer in the other direction deletes a live run's sources. The set must be complete, so
    /// it is not narrowed by caller.
    /// </summary>
    [HttpGet("active-workspaces")]
    [Authorize(Policy = "AgentToken")]
    public async Task<ActionResult<List<string>>> GetActiveWorkspaceSlots(CancellationToken ct)
        => Ok(await runService.GetActiveWorkspaceSlotsAsync(ct));
}
