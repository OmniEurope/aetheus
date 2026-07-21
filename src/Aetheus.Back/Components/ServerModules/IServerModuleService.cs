// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Components.ServerModules;

public interface IServerModuleService
{
    Task<List<ServerModuleDto>> GetByServerIdAsync(int serverId, CancellationToken ct = default);
    Task<ServerModuleDto?> GetByIdAsync(int serverId, int id, CancellationToken ct = default);
    Task<ServerModuleDto> CreateAsync(int serverId, CreateServerModuleRequest request, CancellationToken ct = default);
    Task<ServerModuleDto?> UpdateAsync(int serverId, int id, UpdateServerModuleRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(int serverId, int id, CancellationToken ct = default);
}
