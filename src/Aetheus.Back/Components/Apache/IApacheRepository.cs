// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Apache;

public interface IApacheRepository
{
    Task<ApacheState?> GetStateAsync(int serverId, CancellationToken ct = default);
    Task<List<ApacheModule>> GetModulesAsync(int serverId, CancellationToken ct = default);
    Task<List<ApacheVirtualHost>> GetVirtualHostsAsync(int serverId, CancellationToken ct = default);
    Task<bool> ServerExistsAsync(int serverId, CancellationToken ct = default);
    Task AddTaskAsync(ServerTask task, CancellationToken ct = default);
}
