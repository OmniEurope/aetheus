// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Operations;

/// <summary>
/// Closes an interrupted first deployment, which has no previous colour to roll back to.
/// </summary>
/// <remarks>
/// A rollback restores the colour that was serving before the switch. An initial deployment has none, so
/// the only thing to put back is the recorded upstream configuration. A transaction already switched is
/// refused outright: the deployment succeeded, and undoing it is a retirement, not a rollback.
/// </remarks>
internal sealed class BlueGreenInitialDeployment(IShellRunner shell)
{
    internal async Task<ExecutorResult> ReconcileAsync(
        BlueGreenContext context, BlueGreenJournal journal, string state, string confPath, string reloadCommand,
        int timeoutSeconds, Func<string, TaskLogLevel, Task> onOutput, CancellationToken ct)
    {
        if (state == BlueGreenJournal.Switched)
        {
            await onOutput(
                "The switched transaction has no previous colour, so there is nothing to restore. "
                + "This was an initial deployment; retire it explicitly with a `bluegreen-retire` step instead.",
                TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(1, false);
        }

        if (!await BlueGreenUpstream.RestoreRecordedOrReportAsync(
                shell, context, confPath, reloadCommand, timeoutSeconds, onOutput, ct).ConfigureAwait(false))
            return new ExecutorResult(1, false);
        journal.Clear();
        await onOutput("##aetheus[setvariable name=BLUEGREEN_ROLLED_BACK]true", TaskLogLevel.Info).ConfigureAwait(false);
        await onOutput(
            "Interrupted initial deployment reconciled: the recorded upstream configuration is back and the "
            + "transaction is closed.",
            TaskLogLevel.Warning).ConfigureAwait(false);
        return new ExecutorResult(0, false);
    }
}
