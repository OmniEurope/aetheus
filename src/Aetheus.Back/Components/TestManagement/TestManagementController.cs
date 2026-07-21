// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aetheus.Back.Components.TestManagement;

[ApiController]
[Route("api/test-suites")]
[Authorize]
public class TestManagementController(
    ITestManagementService testService,
    IResourceAuthorizationService authz,
    IPipelineRunService pipelineRunService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<TestSuiteDto>>> GetSuites([FromQuery] int? projectId, CancellationToken ct)
    {
        // F-10: per-project read filter; admins see everything (accessibleIds == null).
        var accessibleIds = await authz.GetAccessibleResourceIdsAsync(User, ResourceType.Project, Permission.Read, ct);
        if (projectId.HasValue && accessibleIds is not null && !accessibleIds.Contains(projectId.Value))
            return Forbid();
        return Ok(await testService.GetSuitesAsync(projectId, accessibleIds, ct));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TestSuiteDetailDto>> GetSuite(int id, CancellationToken ct)
    {
        var result = await testService.GetSuiteDetailAsync(id, ct);
        if (result is null) return NotFound();
        // F-10: enforce project ownership on the returned suite.
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, result.ProjectId, Permission.Read, ct))
            return Forbid();
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<TestSuiteDto>> CreateSuite([FromBody] CreateTestSuiteRequest request, CancellationToken ct)
    {
        var result = await testService.CreateSuiteAsync(request, ct);
        return CreatedAtAction(nameof(GetSuite), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<TestSuiteDto>> UpdateSuite(int id, [FromBody] UpdateTestSuiteRequest request, CancellationToken ct)
    {
        var result = await testService.UpdateSuiteAsync(id, request, ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteSuite(int id, CancellationToken ct)
    {
        var deleted = await testService.DeleteSuiteAsync(id, ct);
        if (!deleted) return NotFound();
        return NoContent();
    }

    [HttpPost("ingest")]
    [Authorize(Policy = "AgentToken")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<ActionResult<TestIngestionResultDto>> IngestResults(
        [FromBody] IngestTestResultsRequest request,
        CancellationToken ct)
    {
        // F-07: derive ProjectId from the authenticated agent's run instead of trusting body input.
        if (!request.PipelineRunId.HasValue) return BadRequest("PipelineRunId is required.");
        var serverIdClaim = User.FindFirst("ServerId")?.Value;
        if (!int.TryParse(serverIdClaim, out var agentServerId)) return Forbid();
        if (!await pipelineRunService.IsServerAssignedToRunAsync(request.PipelineRunId.Value, agentServerId, ct))
            return Forbid();
        var run = await pipelineRunService.GetRunAsync(request.PipelineRunId.Value, ct);
        if (run is null) return NotFound();
        if (run.ProjectId is null) return BadRequest("Pipeline is not bound to a project.");
        var derived = request with { ProjectId = run.ProjectId.Value };

        var result = await testService.IngestTestResultsAsync(derived, ct);
        return Ok(result);
    }
}
