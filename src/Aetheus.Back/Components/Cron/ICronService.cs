// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Cron;

public interface ICronService
{
    Task SaveJobAsync(int serverId, CronJobSaveRequest request, CancellationToken ct = default);
    Task DeleteJobAsync(int serverId, CronJobDeleteRequest request, CancellationToken ct = default);
}
