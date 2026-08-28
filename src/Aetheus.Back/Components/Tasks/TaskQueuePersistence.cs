// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Tasks;

internal static class TaskQueuePersistence
{
    internal static async Task PersistAndNotifyAsync(
        Func<ServerTask, CancellationToken, Task> persist,
        ITaskService taskService,
        ServerTask task,
        CancellationToken ct)
    {
        await persist(task, ct).ConfigureAwait(false);
        await taskService.NotifyTaskQueuedAsync(task, ct: ct).ConfigureAwait(false);
    }
}
