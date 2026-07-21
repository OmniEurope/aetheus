// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Configuration;

namespace Aetheus.Agent.Core.Services;

/// <summary>
/// Detects whether the agent host has the pipeline-runner toolchain installed - the .NET SDK and
/// git, as provisioned by the installer's "pipeline-runner" module. Reported as a server capability
/// so the backend defaults/self-heals the per-server <c>PipelineRunnerEnabled</c> gate from reality
/// instead of authorising every freshly-enrolled box: a runner with no toolchain reports false and
/// is not authorised to receive pipeline steps it could never execute.
/// <para>
/// Like <see cref="DockerProbe"/>, several absolute fallback paths are probed (not just the bare
/// name on <c>PATH</c>) because the agent runs under a hardened systemd unit whose minimal
/// <c>PATH</c> can miss the .NET install location.
/// </para>
/// </summary>
public static class PipelineRunnerProbe
{
    private static readonly string[] DotnetCandidates =
        ["dotnet", "/usr/bin/dotnet", "/usr/local/bin/dotnet", "/usr/share/dotnet/dotnet"];
    private static readonly string[] GitCandidates =
        ["git", "/usr/bin/git", "/usr/local/bin/git"];

    // The toolchain is "available" only when BOTH the .NET SDK and git can actually run - git is
    // needed to clone the pipeline repo, the SDK to build it. (sshpass is deploy-only and optional.)
    public static async Task<bool> IsAvailableAsync(IShellRunner shell, CancellationToken ct = default)
        => await AnyRunnableAsync(shell, DotnetCandidates, ct).ConfigureAwait(false)
           && await AnyRunnableAsync(shell, GitCandidates, ct).ConfigureAwait(false);

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
