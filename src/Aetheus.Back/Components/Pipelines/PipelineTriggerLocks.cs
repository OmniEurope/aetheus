// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// Serializes latest-wins replacement for every webhook entry point in this process.
/// External provider webhooks and pushes to the embedded Git server must share the
/// same lock or concurrent deliveries can both observe and replace the same run.
/// </summary>
internal static class PipelineTriggerLocks
{
    private static readonly RunAdvanceLock Locks = new();

    public static Task<RunAdvanceLock.Releaser> AcquireAsync(int pipelineId, CancellationToken ct) =>
        Locks.AcquireAsync(pipelineId, ct);
}
