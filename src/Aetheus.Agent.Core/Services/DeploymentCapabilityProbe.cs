// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Services;

internal static class DeploymentCapabilityProbe
{
    internal const string HelperPath = "/usr/local/lib/aetheus/deploy-restart";
    internal const string ProbeResponse = "aetheus-deploy-helper-v2";

    internal static async Task<bool> IsAvailableAsync(
        IShellRunner shell,
        CancellationToken cancellationToken) =>
        await IsAvailableAsync(shell, OperatingSystem.IsWindows(), cancellationToken).ConfigureAwait(false);

    internal static async Task<bool> IsAvailableAsync(
        IShellRunner shell,
        bool isWindows,
        CancellationToken cancellationToken)
    {
        if (isWindows)
            return false;

        try
        {
            var result = await shell.RunExecAsync(
                "sudo",
                ["-n", HelperPath, "--probe"],
                cancellationToken,
                AgentRuntimeDefaults.CapabilityProbeTimeout).ConfigureAwait(false);
            return result.ExitCode == 0
                && string.Equals(result.StdOut.Trim(), ProbeResponse, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }
}
