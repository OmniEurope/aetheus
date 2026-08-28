// SPDX-License-Identifier: EUPL-1.2
using System.Runtime.InteropServices;
using Aetheus.Agent.Core.Collectors;


namespace Aetheus.Agent.Core.Services;

/// <summary>
/// S-FEAT-HBDX / S-TECH-CAPX: surfaces the REAL reason a controlled-sudo capability is unavailable, so an
/// operator can diagnose it from the UI instead of SSHing to the box. Two blind spots the hash-based
/// capability derivation cannot see on its own:
/// <list type="bullet">
///   <item>A sudoers drop-in that EXISTS but the agent cannot read (missing read ACL): its hash never
///   reaches the heartbeat, so the derived capability silently shows OFF despite the grant being installed
///   (the exact class that froze package-manage/deployment on the live box).</item>
///   <item>Passwordless sudo blocked by the systemd sandbox (empty CapabilityBoundingSet / NoNewPrivileges):
///   the grant exists but nothing can actually elevate - <c>sudo -n -l</c> is the cheap real-execution
///   probe (S-TECH-CAPX) that catches "capability looks available but every action fails".</item>
/// </list>
/// This is diagnostic-only: it does NOT change how capabilities are derived (still hash-based), it just
/// reports WHY one is off. Linux-only; a no-op elsewhere.
/// </summary>
public static class CapabilityDiagnosticsProbe
{
    public static Task<List<string>> CollectAsync(IShellRunner shell, CancellationToken ct = default)
        => CollectAsync(shell, RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            SudoersHashCollector.KnownSudoersFiles, ct);

    // Testability seam (T13, same discipline as the internal argv builders): the Linux-only branch
    // is exercised from the Windows test host by passing isWindows: false with test-owned paths.
    internal static async Task<List<string>> CollectAsync(
        IShellRunner shell,
        bool isWindows,
        IReadOnlyList<string> sudoersFiles,
        CancellationToken ct,
        Func<string, bool>? fileExists = null,
        Func<string, CancellationToken, Task<byte[]>>? readAllBytesAsync = null)
    {
        var diagnostics = new List<string>();
        if (isWindows) return diagnostics; // sudoers / sudo are Linux-only

        fileExists ??= File.Exists;
        readAllBytesAsync ??= File.ReadAllBytesAsync;

        foreach (var path in sudoersFiles)
        {
            ct.ThrowIfCancellationRequested();
            if (!fileExists(path)) continue;
            try
            {
                await readAllBytesAsync(path, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                diagnostics.Add(
                    $"sudoers drop-in {Path.GetFileName(path)} present but unreadable (agent lacks read ACL) - its capability shows OFF despite the grant");
            }
        }

        // Real-execution probe (S-TECH-CAPX): `sudo -n -l` exits 0 only when passwordless sudo actually
        // works. A non-zero exit while a grant is installed means the sandbox is blocking elevation.
        try
        {
            var result = await shell.RunExecAsync("sudo", ["-n", "-l"], ct, AgentRuntimeDefaults.CapabilityProbeTimeout).ConfigureAwait(false);
            if (result.ExitCode != 0)
                diagnostics.Add(
                    "passwordless sudo unavailable (sudo -n -l failed) - elevated capabilities cannot execute even where a grant is installed");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.Add("passwordless sudo probe could not run (sudo missing or blocked)");
        }

        return diagnostics;
    }
}
