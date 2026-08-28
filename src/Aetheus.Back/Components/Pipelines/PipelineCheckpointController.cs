// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Pipelines;

[ApiController]
[Route("api/pipelines/runs")]
[Authorize]
public sealed class PipelineCheckpointController(
    IPipelineRunService runService,
    IResourceAuthorizationService authz) : ControllerBase
{
    [HttpGet("{runId:int}/checkpoint-resume-preview")]
    public async Task<ActionResult<PipelineCheckpointResumePreviewDto>> GetResumePreview(
        int runId,
        CancellationToken ct)
    {
        var pipelineId = await runService.GetPipelineIdForRunAsync(runId, ct);
        if (pipelineId is null) return NotFound();
        if (!await authz.HasPermissionAsync(User, ResourceType.Pipeline, pipelineId.Value, Permission.Read, ct))
            return Forbid();
        var preview = await runService.GetCheckpointResumePreviewAsync(runId, ct);
        return preview is null ? NotFound() : Ok(preview);
    }
}
