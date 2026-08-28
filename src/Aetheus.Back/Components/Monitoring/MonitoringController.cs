// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Monitoring;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MonitoringController(IMonitoringService monitoringService, IResourceAuthorizationService authz) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardOverviewDto>> GetDashboard(CancellationToken ct)
    {
        var accessibleServerIds = await authz.GetAccessibleResourceIdsAsync(User, ResourceType.Server, Permission.Read, ct);
        var accessibleProjectIds = await authz.GetAccessibleResourceIdsAsync(User, ResourceType.Project, Permission.Read, ct);
        var accessiblePipelineIds = await authz.GetAccessibleResourceIdsAsync(User, ResourceType.Pipeline, Permission.Read, ct);
        return Ok(await monitoringService.GetDashboardAsync(accessibleServerIds, accessibleProjectIds, accessiblePipelineIds, ct));
    }

    [HttpGet("servers/{serverId:int}/metrics")]
    public async Task<ActionResult<List<ServerMetricDto>>> GetServerMetrics(
        int serverId,
        [FromQuery, System.ComponentModel.DataAnnotations.Range(1, 168)] int hours = 24,
        CancellationToken ct = default,
        [FromQuery] DateTime? afterUtc = null,
        [FromQuery, System.ComponentModel.DataAnnotations.Range(1, 2000)] int take = 1_000)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Server, serverId, Permission.Read, ct))
            return Forbid();
        return Ok(await monitoringService.GetServerMetricsAsync(serverId, hours, ct, afterUtc, take));
    }
}
