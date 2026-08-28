// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Tasks;

public interface IPipelineTaskLifecycle
{
    Task CancelActiveTasksAsync(int pipelineRunId, CancellationToken ct = default);
    Task CancelPendingTasksExceptStagesAsync(
        int pipelineRunId,
        IReadOnlyCollection<string> preservedStages,
        CancellationToken ct = default);
}
