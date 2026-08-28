// SPDX-License-Identifier: EUPL-1.2
using System.Runtime.InteropServices;


namespace Aetheus.Agent.Core.Services;

/// <summary>
/// Detects whether the agent host can actually RUN containers. Reported as a server capability so
/// the backend can gate container-isolated pipeline stages: a stage requesting
/// <c>isolation: container</c> only schedules onto a runner where this returns true (otherwise the
/// run is blocked, never silently downgraded to direct process execution).
/// <para>
/// "Can run containers" means more than "the binary exists": the probe runs <c>docker info</c>,
/// which only exits 0 when the daemon is reachable AND the agent user has socket permission (i.e.
/// is in the <c>docker</c> group). A box with the CLI installed but the agent outside the docker
/// group correctly reports <c>false</c> - that's the fail-closed signal an operator needs.
/// </para>
/// <para>
/// It also probes several absolute paths, not just <c>docker</c> on <c>PATH</c>: the agent runs
/// under a hardened systemd unit whose minimal <c>PATH</c> excludes <c>/snap/bin</c>, so a
/// snap-installed Docker would otherwise be invisible.
/// </para>
/// </summary>
public static class DockerProbe
{
    // Probed in order; the first that reports a reachable daemon wins. "docker" (PATH) first so a
    // correctly-configured host resolves immediately; absolute fallbacks cover minimal-PATH units
    // and snap installs.
    private static readonly string[] LinuxCandidates =
        ["docker", "/usr/bin/docker", "/usr/local/bin/docker", "/snap/bin/docker"];

    public static async Task<bool> IsAvailableAsync(IShellRunner shell, CancellationToken ct = default)
    {
        var candidates = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ["docker"]
            : LinuxCandidates;

        foreach (var docker in candidates)
        {
            if (await ProbeAsync(shell, docker, ct).ConfigureAwait(false))
                return true;
        }
        return false;
    }

    private static async Task<bool> ProbeAsync(IShellRunner shell, string docker, CancellationToken ct)
    {
        try
        {
            // `info --format {{.ServerVersion}}` is tiny and exits 0 only when the daemon answers.
            var result = await shell.RunExecAsync(
                docker, ["info", "--format", "{{.ServerVersion}}"], ct, AgentRuntimeDefaults.CapabilityProbeTimeout).ConfigureAwait(false);
            return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StdOut);
        }
        catch (Exception)
        {
            // Binary not found on this candidate path, daemon down, or permission denied - try the next.
            return false;
        }
    }
}
