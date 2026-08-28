// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Operations;

internal static class LinuxAgentPostureUpgradeRequester
{
    internal static string BuildQualifiedRequest(ResolvedAgentReleaseArchive release) =>
        string.Join('\t',
            release.SoftwareVersion,
            release.Archive.FileName,
            release.Archive.SizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            release.Archive.Sha256.ToLowerInvariant(),
            release.Commit);

    internal static async Task<bool> TryRequestAsync(
        string workDirectory,
        string postureVersionPath,
        string requestPath,
        string expectedVersion,
        string qualifiedRequest,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(postureVersionPath)
            || !string.Equals(
                (await File.ReadAllTextAsync(postureVersionPath, cancellationToken)
                    .ConfigureAwait(false)).Trim(),
                expectedVersion,
                StringComparison.Ordinal))
        {
            await onOutput(
                "Self-update refused: the installed Linux integration posture does not support autonomous full upgrades.",
                TaskLogLevel.Error).ConfigureAwait(false);
            return false;
        }

        AgentUpdateRecoveryState.MarkPending(workDirectory);
        var temporaryRequest = requestPath + ".tmp";
        await File.WriteAllTextAsync(
            temporaryRequest,
            qualifiedRequest + "\n",
            cancellationToken).ConfigureAwait(false);
        File.Move(temporaryRequest, requestPath, overwrite: true);
        await onOutput(
            "Full Linux upgrade requested; the system supervisor will update binaries, systemd policy and privileged helpers.",
            TaskLogLevel.Info).ConfigureAwait(false);
        return true;
    }
}
