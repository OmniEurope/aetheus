// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Artifacts;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ArtifactsController(
    IArtifactService artifactService,
    IResourceAuthorizationService authz) : ControllerBase
{
    [HttpGet("project/{projectId:int}")]
    public async Task<ActionResult<PaginatedResult<PipelineArtifactDto>>> GetProjectArtifacts(
        int projectId, [FromQuery] ProjectArtifactsRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, projectId, Permission.Read, ct))
            return Forbid();

        return Ok(await artifactService.GetProjectArtifactsAsync(projectId, request, ct));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PipelineArtifactDto>> GetArtifact(int id, CancellationToken ct)
    {
        var (artifact, failure) = await GetAuthorizedArtifactAsync(id, Permission.Read, ct);
        if (failure is not null) return failure;

        return Ok(artifact!);
    }

    [HttpGet("{id:int}/download")]
    public async Task<IActionResult> DownloadArtifact(int id, CancellationToken ct)
    {
        var (artifact, failure) = await GetAuthorizedArtifactAsync(id, Permission.Read, ct);
        if (failure is not null) return failure;

        var stream = await artifactService.DownloadArtifactAsync(id, ct);
        if (stream is null) return NotFound();

        return File(stream, "application/zip", $"{artifact!.Name}.zip");
    }

    // Cross-agent deploy download. AgentToken-authorised, IDOR-safe: the service checks the agent is
    // assigned to the DEPLOY run it passes (not the artifact's build run - scenario 3) and that the
    // artifact's project org equals the agent server's org. The deploy run id travels as a query param.
    [HttpGet("agent-download/{id:int}")]
    [Authorize(Policy = "AgentToken")]
    public async Task<IActionResult> AgentDownloadArtifact(int id, [FromQuery] int runId, CancellationToken ct)
    {
        var serverIdClaim = User.FindFirst("ServerId")?.Value;
        if (!int.TryParse(serverIdClaim, out var agentServerId)) return Forbid();

        var (status, stream, fileName) = await artifactService.OpenArtifactForAgentAsync(id, runId, agentServerId, ct);
        return status switch
        {
            DeployDownloadStatus.Ok => File(stream!, "application/zip", fileName),
            DeployDownloadStatus.Forbidden => Forbid(),
            _ => NotFound()
        };
    }

    // Upload ceiling rationale (DoS analysis): the endpoint is [Authorize(AgentToken)] - only an
    // enrolled agent can POST here, and IsAgentAssignedToRunAsync further pins it to a run the agent is
    // actually executing, so the surface is "a compromised/enrolled agent", not the open internet.
    // Sizing: a `docker save` of a .NET runtime image measures ~700 MiB; gzipped it still clears 500 MiB,
    // which is why the old 500 MiB cap 413'd the very payload container deploy needs - 1 GiB is the
    // smallest power-of-two ceiling that admits a realistic container artifact with headroom. Tune the
    // const if your images are larger; it bounds the per-request body buffer either way.
    private const long MaxArtifactUploadBytes = 1024L * 1024 * 1024; // 1 GiB

    [HttpPost("upload/{runId:int}")]
    [Authorize(Policy = "AgentToken")]
    [RequestSizeLimit(MaxArtifactUploadBytes)]
    public async Task<ActionResult<PipelineArtifactDto>> UploadArtifact(
        int runId, [FromQuery] string name, [FromQuery] string? stageName, CancellationToken ct)
    {
        var serverIdClaim = User.FindFirst("ServerId")?.Value;
        if (!int.TryParse(serverIdClaim, out var agentServerId)) return Forbid();
        if (!await artifactService.IsAgentAssignedToRunAsync(runId, agentServerId, ct)) return Forbid();

        if (string.IsNullOrWhiteSpace(name) || name.Length > 100) return BadRequest("Invalid artifact name");
        // Bound stageName to the same limit as PublishArtifactRequest.StageName ([StringLength(200)]):
        // these query params bypass the DTO validator, so the cap is enforced here before persistence.
        if (stageName is { Length: > 200 }) return BadRequest("Invalid stage name");

        // The agent uploads a seekable ZIP and therefore always provides Content-Length. Requiring
        // it lets quota eviction run before the body is streamed and prevents quota checks from
        // accepting an artifact whose size is unknown.
        if (Request.ContentLength is not long contentLength)
            return StatusCode(StatusCodes.Status411LengthRequired, "Content-Length is required for artifact uploads.");

        var result = await artifactService.PublishArtifactAsync(runId, name, stageName, contentLength, Request.Body, ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    [HttpPost("{id:int}/promote")]
    public async Task<ActionResult<PipelineArtifactDto>> PromoteArtifact(
        int id, [FromBody] PromoteArtifactRequest request, CancellationToken ct)
    {
        var (_, failure) = await GetAuthorizedArtifactAsync(id, Permission.Write, ct);
        if (failure is not null) return failure;

        if (string.IsNullOrWhiteSpace(request.EnvironmentName))
            return BadRequest("EnvironmentName is required");

        var result = await artifactService.PromoteToEnvironmentAsync(id, request.EnvironmentName, ct);
        return Ok(result!);
    }

    private async Task<(PipelineArtifactDto? Artifact, ActionResult? Failure)> GetAuthorizedArtifactAsync(
        int artifactId,
        Permission permission,
        CancellationToken ct)
    {
        var artifact = await artifactService.GetArtifactAsync(artifactId, ct);
        if (artifact is null) return (null, NotFound());
        if (artifact.ProjectId is { } projectId
            && !await authz.HasPermissionAsync(User, ResourceType.Project, projectId, permission, ct))
            return (null, Forbid());
        return (artifact, null);
    }
}
