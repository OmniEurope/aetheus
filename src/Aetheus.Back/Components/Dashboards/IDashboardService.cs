// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Components.Dashboards;

public interface IDashboardService
{
    Task<List<DashboardDto>> GetUserDashboardsAsync(int userId, CancellationToken ct = default);
    Task<DashboardDto?> GetDashboardAsync(int id, int userId, CancellationToken ct = default);
    Task<DashboardDto> CreateDashboardAsync(int userId, CreateDashboardRequest request, CancellationToken ct = default);
    Task<DashboardDto?> UpdateDashboardAsync(int id, int userId, UpdateDashboardRequest request, CancellationToken ct = default);
    Task<bool> DeleteDashboardAsync(int id, int userId, CancellationToken ct = default);
}
