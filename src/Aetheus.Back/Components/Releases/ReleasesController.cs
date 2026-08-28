// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Microsoft.AspNetCore.RateLimiting;

namespace Aetheus.Back.Components.Releases;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReleasesController(
    IReleaseService service,
    IResourceAuthorizationService authz,
    IConfiguration configuration,
    IPipelineRunService pipelineRunService) : ControllerBase
{
    /// <summary>
    /// Releases of every project a server is involved in.
    ///
    /// Relocated from ServersController with its route unchanged: the endpoint is published as
    /// <c>api/servers/{serverId}/releases</c> and the frontend calls it by that path, so moving the
    /// controller must not move the URL. The authorization is still the SERVER's Read permission -
    /// the resource being scoped is the server, whatever module now serves the request.
    ///
    /// A360-18: this was the only list endpoint of the module that returned an unbounded list, on a
    /// table built for long retention. It is paginated now, like every other list route. The response
    /// shape changed from List to PaginatedResult, and its single consumer (the front's ReleasesList in
    /// server scope) moved with it - there is no other caller, so no compatibility route is kept for a
    /// shape nobody else reads.
    /// </summary>
    [HttpGet("/api/servers/{serverId:int}/releases")]
    public async Task<ActionResult<PaginatedResult<ReleaseDto>>> GetServerReleases(
        int serverId, [FromQuery] PaginationRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Read, ct))
            return Forbid();

        return Ok(await service.GetServerReleasesAsync(serverId, request, ct));
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedResult<ReleaseDto>>> GetReleases(
        [FromQuery] int? projectId, [FromQuery] PaginationRequest request, CancellationToken ct)
    {
        var accessibleIds = await authz.GetAccessibleResourceIdsAsync(User, ResourceType.Release, Permission.Read, ct);
        if (accessibleIds is { Count: 0 }) return Ok(new PaginatedResult<ReleaseDto>());
        return Ok(await service.GetReleasesAsync(projectId, request, accessibleIds, ct));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ReleaseDto>> GetRelease(int id, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Release, id, Permission.Read, ct))
            return Forbid();
        var release = await service.GetReleaseAsync(id, ct);
        if (release is null) return NotFound();
        return Ok(release);
    }

    [HttpGet("{id:int}/rollback-preview")]
    public async Task<ActionResult<ReleaseRollbackPreviewDto>> GetRollbackPreview(int id, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Release, id, Permission.Read, ct))
            return Forbid();
        return Ok(await service.GetRollbackPreviewAsync(id, ct));
    }

    [HttpGet("by-run/{runId:int}")]
    public async Task<ActionResult<List<ReleaseDto>>> GetByRun(int runId, CancellationToken ct)
    {
        var releases = await service.GetReleasesByRunAsync(runId, ct);
        // Per-resource filtering, consistent with GetReleases/GetRelease: a run may reference
        // releases on projects the caller can't access - never leak those. null = full access.
        var accessibleIds = await authz.GetAccessibleResourceIdsAsync(User, ResourceType.Release, Permission.Read, ct);
        if (accessibleIds is not null)
            releases = releases.Where(r => accessibleIds.Contains(r.Id)).ToList();
        return Ok(releases);
    }

    [HttpPost("sync/{projectId:int}")]
    public async Task<ActionResult<List<ReleaseDto>>> SyncReleases(int projectId, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, projectId, Permission.Write, ct))
            return Forbid();
        return Ok(await service.SyncReleasesAsync(projectId, ct));
    }

    [HttpPost("{id:int}/build")]
    public async Task<ActionResult<ReleaseDto>> TriggerBuild(int id, [FromBody] TriggerReleaseBuildRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Release, id, Permission.Write, ct))
            return Forbid();
        var result = await service.TriggerReleaseBuildAsync(id, request, ct);
        return AcceptedAtAction(nameof(GetRelease), new { id = result.Id }, result);
    }

    [HttpPost("{id:int}/rollback")]
    public async Task<ActionResult<ReleaseRollbackDto>> Rollback(int id, [FromBody] RollbackReleaseRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Release, id, Permission.Write, ct))
            return Forbid();
        var release = await service.GetReleaseAsync(id, ct);
        if (release is null) return NotFound();
        // A rollback can redeploy an application and, when explicitly requested, restore its data.
        // Release.Write alone is therefore insufficient: the caller must also be allowed to operate
        // on the owning project.
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, release.ProjectId, Permission.Write, ct))
            return Forbid();
        if (!await authz.HasPermissionAsync(User, ResourceType.Pipeline, request.PipelineId, Permission.Write, ct))
            return Forbid();
        var result = await service.RollbackReleaseAsync(id, request, ct);
        return AcceptedAtAction(nameof(GetRelease), new { id }, result);
    }

    [HttpPost("{id:int}/promote")]
    public async Task<ActionResult<ReleaseDto>> Promote(int id, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Release, id, Permission.Write, ct))
            return Forbid();
        var result = await service.PromoteReleaseAsync(id, ct);
        return AcceptedAtAction(nameof(GetRelease), new { id = result.Id }, result);
    }

    [HttpPost("create")]
    [Authorize(Policy = "AgentToken")]
    public async Task<ActionResult<ReleaseDto>> CreateRelease([FromBody] CreateReleaseRequest request, [FromQuery] int projectId, CancellationToken ct)
    {
        if (request.PipelineRunId is not > 0)
            return BadRequest(new ApiError { Message = "A valid pipeline run is required." });

        var serverIdClaim = User.FindFirst("ServerId")?.Value;
        if (!int.TryParse(serverIdClaim, out var agentServerId))
            return Forbid();

        var pipelineRunId = request.PipelineRunId.Value;
        if (!await pipelineRunService.IsServerAssignedToRunAsync(pipelineRunId, agentServerId, ct))
            return Forbid();

        var runContext = await pipelineRunService.GetRunPipelineContextAsync(pipelineRunId, ct);
        if (runContext?.ProjectId != projectId)
            return Forbid();

        var result = await service.CreateReleaseFromPipelineAsync(
            projectId, pipelineRunId, request.Version, request.Changelog,
            request.CommitHash, request.TagName, request.BranchName, request.ArtifactPipelineRunId,
            request.Deployed, ct);
        return CreatedAtAction(nameof(GetRelease), new { id = result.Id }, result);
    }

    [HttpPost("webhook")]
    [AllowAnonymous]
    [EnableRateLimiting("webhook")]
    [RequestSizeLimit(64 * 1024)]
    public async Task<IActionResult> Webhook(CancellationToken ct)
    {
        var secret = configuration["Webhook:Secret"];
        if (string.IsNullOrEmpty(secret))
            return StatusCode(503, "Webhook not configured");

        var signature = Request.Headers["X-Hub-Signature-256"].FirstOrDefault()
                     ?? Request.Headers["X-Gitlab-Token"].FirstOrDefault();

        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync(ct);

        if (!service.ValidateWebhookSignature(signature, secret, rawBody))
            return Unauthorized();

        var payload = System.Text.Json.JsonSerializer.Deserialize<WebhookPayload>(rawBody);
        if (payload is null) return BadRequest();

        await service.HandleWebhookAsync(payload, ct);
        return Ok();
    }
}
