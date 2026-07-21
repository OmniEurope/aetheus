// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Cron;

public interface ICronRepository
{
    Task<bool> ServerExistsAsync(int serverId, CancellationToken ct = default);
    Task AddTaskAsync(ServerTask task, CancellationToken ct = default);
}
