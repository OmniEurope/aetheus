// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Components.Monitoring;

public interface IMonitoringService
{
    Task<DashboardOverviewDto> GetDashboardAsync(
        List<int>? accessibleServerIds = null,
        List<int>? accessibleProjectIds = null,
        CancellationToken ct = default);
    Task<DashboardOverviewDto> GetDashboardAsync(
        List<int>? accessibleServerIds,
        List<int>? accessibleProjectIds,
        List<int>? accessiblePipelineIds,
        CancellationToken ct);
    Task<List<ServerMetricDto>> GetServerMetricsAsync(int serverId, int hours, CancellationToken ct = default);
}
