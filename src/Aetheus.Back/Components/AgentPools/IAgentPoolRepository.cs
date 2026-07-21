// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.AgentPools;

public interface IAgentPoolRepository
{
    Task<(List<AgentPool> Items, int TotalCount)> GetPoolsPagedAsync(
        string? search, int page, int pageSize, List<int>? accessibleIds = null, CancellationToken ct = default);
    Task<AgentPool?> GetPoolWithServersAsync(int id, CancellationToken ct = default);
    Task<AgentPool?> FindPoolAsync(int id, CancellationToken ct = default);
    Task<AgentPool?> FindByNameAsync(string name, CancellationToken ct = default);
    Task AddPoolAsync(AgentPool pool, CancellationToken ct = default);
    Task RemovePoolAsync(AgentPool pool, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
