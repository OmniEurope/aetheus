// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.ServerModules;

public interface IServerModuleRepository
{
    Task<List<ServerModule>> GetByServerIdAsync(int serverId, CancellationToken ct = default);
    Task<ServerModule?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<ServerModule> AddAsync(ServerModule module, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task RemoveAsync(ServerModule module, CancellationToken ct = default);
}
