// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// Tells the agent which run a blue-green step belongs to, and whether that run holds the environment
/// lock (decision of 2026-10-02). The agent records the run in the transaction journal: a rollback
/// then undoes only its own run's deployment, and with the lock an open transaction of another run is
/// an orphan it may undo before starting, instead of refusing every deployment until someone clears it.
/// </summary>
internal static class BlueGreenRunOwnership
{
    internal const string OwnerRunVariable = "AETHEUS_BG_OWNER_RUN";
    internal const string ExclusiveVariable = "AETHEUS_BG_EXCLUSIVE";

    internal static async Task StampAsync(
        Dictionary<string, string> variables, int runId, IPipelineResourceLockRepository? locks, CancellationToken ct)
    {
        variables[OwnerRunVariable] = runId.ToString(CultureInfo.InvariantCulture);
        if (locks is not null && await locks.HoldsAnyAsync(runId, ct).ConfigureAwait(false))
            variables[ExclusiveVariable] = "true";
    }
}
