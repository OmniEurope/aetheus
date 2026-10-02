// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// PLAN-003 lot 20 / D26: how long each stage and step of a pipeline usually takes. Beside
/// <see cref="PipelinesController"/> rather than inside it because that controller is at its size
/// budget. The run page used to rebuild this reference by downloading the whole run history of the
/// pipeline and keeping only the last comparable run; this answers with an average over the last
/// successful runs, computed where the data is.
/// </summary>
[ApiController]
[Route("api/pipelines/{id:int}/runs/{runId:int}/stage-baselines")]
[Authorize]
[ProducesResponseType(StatusCodes.Status200OK)]
public class PipelineRunBaselinesController(
    IRunStageBaselineService baselines,
    IResourceAuthorizationService authz) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<RunStageBaselinesDto>> Get(int id, int runId, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Pipeline, id, Permission.Read, ct))
            return Forbid();

        return Ok(await baselines.GetAsync(id, runId, ct));
    }
}
