// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Dashboards;

public interface IDashboardRepository
{
    Task<List<Dashboard>> GetByUserIdAsync(int userId, CancellationToken ct = default);
    Task<Dashboard?> GetDetailAsync(int id, CancellationToken ct = default);
    Task<Dashboard?> FindAsync(int id, CancellationToken ct = default);
    Task AddAsync(Dashboard dashboard, CancellationToken ct = default);
    Task RemoveAsync(Dashboard dashboard, CancellationToken ct = default);
    Task ClearDefaultsAsync(int userId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
