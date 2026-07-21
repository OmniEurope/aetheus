// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aetheus.Back.Components.AppMonitoring;

/// <summary>
/// Read/manage surface for OTLP telemetry (PLAN-001 phases 2-4): ingestion keys, metric series + thresholds,
/// logs, errors. RBAC is inherited from the monitored app's parent Project, like <see cref="AppMonitoringController"/>.
/// </summary>
[ApiController]
[Route("api/appmonitoring")]
[Authorize]
public sealed class AppTelemetryController(
    IAppTelemetryService telemetry,
    IAppMonitoringService monitoring,
    IResourceAuthorizationService authz) : ControllerBase
{
    // --- ingestion key (Write) ---

    [HttpPost("apps/{id:int}/ingest-key")]
    public async Task<ActionResult<IngestKeyResponse>> GenerateIngestKey(int id, CancellationToken ct)
    {
        if (await GateAsync(id, Permission.Write, ct) is { } fail) return fail;
        var key = await telemetry.GenerateIngestKeyAsync(id, ct);
        return key is null ? NotFound() : Ok(key);
    }

    [HttpDelete("apps/{id:int}/ingest-key")]
    public async Task<IActionResult> RevokeIngestKey(int id, CancellationToken ct)
    {
        if (await GateAsync(id, Permission.Write, ct) is { } fail) return fail;
        return await telemetry.RevokeIngestKeyAsync(id, ct) ? NoContent() : NotFound();
    }

    // --- metrics (Read) ---

    [HttpGet("apps/{id:int}/metrics/names")]
    public async Task<ActionResult<List<string>>> GetMetricNames(int id, CancellationToken ct)
    {
        if (await GateAsync(id, Permission.Read, ct) is { } fail) return fail;
        return Ok(await telemetry.GetMetricNamesAsync(id, ct));
    }

    [HttpGet("apps/{id:int}/metrics/series")]
    public async Task<ActionResult<MetricSeriesDto>> GetMetricSeries(
        int id, [FromQuery] string metric, [FromQuery] int hours = 24, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(metric))
            return BadRequest(new ApiError { Message = "metric query parameter is required." });
        if (await GateAsync(id, Permission.Read, ct) is { } fail) return fail;
        return Ok(await telemetry.GetMetricSeriesAsync(id, metric, hours, ct));
    }

    // --- thresholds ---

    [HttpGet("apps/{id:int}/thresholds")]
    public async Task<ActionResult<List<AppMetricThresholdDto>>> GetThresholds(int id, CancellationToken ct)
    {
        if (await GateAsync(id, Permission.Read, ct) is { } fail) return fail;
        return Ok(await telemetry.GetThresholdsAsync(id, ct));
    }

    [HttpPost("apps/{id:int}/thresholds")]
    public async Task<ActionResult<AppMetricThresholdDto>> CreateThreshold(int id, [FromBody] CreateMetricThresholdRequest request, CancellationToken ct)
    {
        if (await GateAsync(id, Permission.Write, ct) is { } fail) return fail;
        return Ok(await telemetry.CreateThresholdAsync(id, request, ct));
    }

    [HttpDelete("thresholds/{thresholdId:int}")]
    public async Task<IActionResult> DeleteThreshold(int thresholdId, CancellationToken ct)
    {
        var appId = await telemetry.GetThresholdAppIdAsync(thresholdId, ct);
        if (appId is null)
            return NotFound();
        if (await GateAsync(appId.Value, Permission.Write, ct) is { } fail) return fail;
        return await telemetry.DeleteThresholdAsync(thresholdId, ct) ? NoContent() : NotFound();
    }

    // --- logs (Read) ---

    [HttpGet("apps/{id:int}/logs")]
    public async Task<ActionResult<PaginatedResult<AppLogEntryDto>>> GetLogs(
        int id, [FromQuery] int hours = 24, [FromQuery] int? minSeverity = null,
        [FromQuery] string? search = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 100, CancellationToken ct = default)
    {
        if (await GateAsync(id, Permission.Read, ct) is { } fail) return fail;
        return Ok(await telemetry.GetLogsAsync(id, hours, minSeverity, search, page, pageSize, ct));
    }

    // --- errors (Read) ---

    [HttpGet("apps/{id:int}/errors")]
    public async Task<ActionResult<PaginatedResult<AppErrorEventDto>>> GetErrors(
        int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        if (await GateAsync(id, Permission.Read, ct) is { } fail) return fail;
        return Ok(await telemetry.GetErrorsAsync(id, page, pageSize, ct));
    }

    /// <summary>Resolves the app's parent Project and checks the required permission. Returns a failing
    /// result (NotFound/Forbid) to short-circuit, or null when access is granted.</summary>
    private async Task<ActionResult?> GateAsync(int appId, Permission required, CancellationToken ct)
    {
        var projectId = await monitoring.GetAppProjectIdAsync(appId, ct);
        if (projectId is null)
            return NotFound();
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, projectId.Value, required, ct))
            return Forbid();
        return null;
    }
}
