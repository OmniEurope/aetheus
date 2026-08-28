// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Tasks;

public interface ITaskQueueNotifier
{
    Task NotifyTaskQueuedAsync(
        ServerTask task,
        string? serverNameOverride = null,
        CancellationToken ct = default);
}
