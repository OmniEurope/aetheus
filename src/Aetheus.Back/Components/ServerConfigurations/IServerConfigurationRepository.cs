// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.ServerConfigurations;

public interface IServerConfigurationRepository
{
    Task<Server?> GetServerWithDockerAndServicesReadOnlyAsync(int id, CancellationToken ct = default);

    Task<Server?> GetServerWithDockerAndServicesAsync(int id, CancellationToken ct = default);

    Task AddTaskAsync(ServerTask task);

    Task SaveChangesAsync(CancellationToken ct = default);
}
