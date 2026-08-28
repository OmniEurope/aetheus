// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Services;

/// <summary>
/// Detects whether the agent host can prepare pipeline workspaces with Git. Application SDKs are
/// resolved inside immutable OCI images and Docker availability is reported independently by
/// <see cref="DockerProbe"/>. Reported as a server capability
/// so the backend defaults/self-heals the per-server <c>PipelineRunnerEnabled</c> gate from reality
/// instead of authorising every freshly-enrolled box: a runner with no toolchain reports false and
/// is not authorised to receive pipeline steps it could never execute.
/// <para>
/// Several absolute fallback paths are probed because the agent runs under a hardened systemd unit
/// whose minimal <c>PATH</c> can miss the Git install location.
/// </para>
/// </summary>
public static class PipelineRunnerProbe
{
    private static readonly string[] GitCandidates =
        ["git", "/usr/bin/git", "/usr/local/bin/git"];

    // Host pipeline preparation requires Git only. Each application toolchain is resolved later from
    // the checked-out repository and executed in its digest-pinned container.
    public static async Task<bool> IsAvailableAsync(IShellRunner shell, CancellationToken ct = default)
        => await AnyRunnableAsync(shell, GitCandidates, ct).ConfigureAwait(false);

    private static async Task<bool> AnyRunnableAsync(IShellRunner shell, string[] candidates, CancellationToken ct)
    {
        foreach (var bin in candidates)
        {
            if (await ProbeAsync(shell, bin, ct).ConfigureAwait(false))
                return true;
        }
        return false;
    }

    private static async Task<bool> ProbeAsync(IShellRunner shell, string bin, CancellationToken ct)
    {
        try
        {
            // `--version` exits 0 and prints when the binary is present and runnable; a missing binary
            // throws fast (file not found), so absent toolchains never burn the full timeout.
            var result = await shell.RunExecAsync(bin, ["--version"], ct, AgentRuntimeDefaults.CapabilityProbeTimeout).ConfigureAwait(false);
            return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StdOut);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
