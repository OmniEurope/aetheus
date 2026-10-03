// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Operations;

/// <summary>
/// Which pipeline run a blue-green transaction belongs to (decision of 2026-10-02). The control plane
/// names the run on every blue-green task and says whether that run holds the environment lock. With
/// the lock, no other run can be mid-deployment on the environment, so a transaction another run left
/// open is an orphan: its run ended without its rollback (a crash, a cancellation). It is rolled back
/// before a new deployment starts, instead of wedging every deployment until someone clears it by hand.
/// Without the lock nothing can be assumed, and the new deployment is refused before it rebuilds the
/// colour the open transaction would fall back to.
/// </summary>
internal static class BlueGreenTransactionOwnership
{
    internal const string OwnerRunVariable = "AETHEUS_BG_OWNER_RUN";
    internal const string ExclusiveVariable = "AETHEUS_BG_EXCLUSIVE";

    internal static string? OwnerOf(IReadOnlyDictionary<string, string> envVars) =>
        envVars.GetValueOrDefault(OwnerRunVariable)?.Trim() is { Length: > 0 } owner ? owner : null;

    internal static bool HoldsEnvironmentLock(IReadOnlyDictionary<string, string> envVars) =>
        string.Equals(envVars.GetValueOrDefault(ExclusiveVariable)?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    internal static bool IsOpen(BlueGreenJournal journal) =>
        journal.Exists && journal.State is BlueGreenJournal.Prepared or BlueGreenJournal.Switched;

    /// <summary>An open transaction this run did not start. One written before owners were recorded counts.</summary>
    internal static bool IsAnotherRuns(BlueGreenJournal journal, string? owner) =>
        IsOpen(journal) && !string.Equals(journal.OwnerRun, owner, StringComparison.Ordinal);

    /// <summary>
    /// Before a deployment rebuilds a colour: refuses when another run's transaction is open and the
    /// environment is not locked, rolls it back when it is. Null when the deployment may go on.
    /// </summary>
    internal static async Task<ExecutorResult?> SettleAnotherRunsTransactionAsync(
        BlueGreenJournal journal, IReadOnlyDictionary<string, string> envVars,
        Func<Task<ExecutorResult>> rollbackOrphan, Func<string, TaskLogLevel, Task> onOutput)
    {
        if (!IsAnotherRuns(journal, OwnerOf(envVars))) return null;
        var orphanRun = journal.OwnerRun ?? "unknown";
        if (!HoldsEnvironmentLock(envVars))
        {
            await onOutput(
                $"Run {orphanRun} left a deployment transaction open (state={journal.State}); its rollback must run "
                + "before another deployment rebuilds the colour it would fall back to.",
                TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(1, false);
        }
        await onOutput(
            $"Run {orphanRun} ended with its deployment transaction open (state={journal.State}); this run holds the "
            + "environment, so that transaction is an orphan and is rolled back first.",
            TaskLogLevel.Warning).ConfigureAwait(false);
        var recovery = await rollbackOrphan().ConfigureAwait(false);
        return recovery.ExitCode == 0 ? null : recovery;
    }

    /// <summary>A rollback refuses a transaction another run opened. Null when it is this run's, or unrecorded.</summary>
    internal static async Task<ExecutorResult?> RefuseAnotherRunsRollbackAsync(
        BlueGreenJournal journal, IReadOnlyDictionary<string, string> envVars, Func<string, TaskLogLevel, Task> onOutput)
    {
        if (journal.OwnerRun is not { } openedBy || OwnerOf(envVars) is not { } owner
            || string.Equals(openedBy, owner, StringComparison.Ordinal))
            return null;
        await onOutput(
            $"The open deployment transaction belongs to run {openedBy}, not to this run ({owner}); it is not this run's to undo.",
            TaskLogLevel.Error).ConfigureAwait(false);
        return new ExecutorResult(1, false);
    }
}
