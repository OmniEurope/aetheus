// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Components.AgentPools;

public interface IAgentPoolService
{
    Task<PaginatedResult<AgentPoolDto>> GetPoolsAsync(PaginationRequest request, List<int>? accessibleIds = null, CancellationToken ct = default);
    Task<AgentPoolDto?> GetPoolAsync(int id, CancellationToken ct = default);
    Task<AgentPoolDto> CreatePoolAsync(CreateAgentPoolRequest request, CancellationToken ct = default);
    Task<AgentPoolDto?> UpdatePoolAsync(int id, UpdateAgentPoolRequest request, CancellationToken ct = default);
    Task<bool> DeletePoolAsync(int id, CancellationToken ct = default);
}
