// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Executors;

/// <summary>
/// Decides which host directory a container may bind as the run workspace.
///
/// The backend proposes one through <c>AETHEUS_HOST_WORKSPACE</c>, and a proposal is only honoured
/// when it matches the path this agent would have computed itself. Anything else falls back to the
/// agent's own per-container directory: the variable travels with a task, so accepting it as given
/// would let a crafted task bind an arbitrary host path into a container.
/// </summary>
internal static class HostWorkspaceResolver
{
    /// <summary>Durable Linux work directory: the installer's WORK_DIR and the agent's own default.
    /// Linux moved off the OS temp directory because systemd's <c>PrivateTmp=true</c> hands the service
    /// a fresh tmpfs on every start, which destroyed the workspace of a live run whenever the agent
    /// restarted (a self-update between two jobs was enough, production run 2185).</summary>
    internal const string LinuxAgentWorkDirectory = "/var/lib/aetheus-agent";

    /// <summary>
    /// The path the backend is expected to have proposed for this workspace key. Must mirror
    /// <c>PipelineCommandBuilder.GetDefaultWorkspace</c>: a mismatch does not fail loudly, it silently
    /// falls back to the per-container directory, so the two definitions move together.
    /// </summary>
    internal static string ExpectedWorkspace(int workspaceKey)
    {
        var mixed = (uint)workspaceKey * 2654435761u;
        var slot = mixed.ToString("x8");
        return OperatingSystem.IsWindows()
            ? Path.Combine(@"C:\w", slot, "s")
            : Path.Combine(LinuxAgentWorkDirectory, "w", slot, "s");
    }

    internal static string Resolve(int workspaceKey, string? candidate, string workDirectory)
    {
        var fallback = Path.Combine(workDirectory, "cw", workspaceKey.ToString());
        if (string.IsNullOrWhiteSpace(candidate)) return fallback;

        try
        {
            var normalizedCandidate = Path.GetFullPath(candidate);
            var normalizedExpected = Path.GetFullPath(ExpectedWorkspace(workspaceKey));
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return string.Equals(normalizedCandidate, normalizedExpected, comparison)
                ? normalizedCandidate
                : fallback;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return fallback;
        }
    }
}
