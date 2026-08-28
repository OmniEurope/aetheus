// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Aetheus.Agent.Core.Operations;

/// <summary>
/// S-FEAT-W8KN: typed install/uninstall of an OS package on a managed server. Linux only - runs
/// <c>sudo -n /usr/bin/apt-get install|remove -y &lt;pkg&gt;</c> against the argv-exact
/// <c>/etc/sudoers.d/aetheus-package</c> allow-list (provisioned by the server-management module's
/// <c>--enable-package-manage</c> flag). The package name is re-validated against the closed
/// <see cref="Aetheus.Shared.Constants.ManageablePackages"/> list before the call, and a package
/// outside the sudoers allow-list is refused by sudo at the OS level - defence in depth.
/// </summary>
public sealed class PackageOperationExecutor(
    IOptions<AetheusAgentOptions> options,
    ILogger<PackageOperationExecutor> logger) : IOperationExecutor
{
    private readonly AetheusAgentOptions _options = options.Value;

    public bool CanHandle(OperationKind kind) => kind is
        OperationKind.ServiceInstall or
        OperationKind.ServiceUninstall;

    public async Task<ExecutorResult> ExecuteAsync(
        OperationKind kind, string target, int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput, CancellationToken cancellationToken)
    {
        var targetFailure = await OperationExecutorFailure.ValidateTargetAsync(
            kind, target, onOutput, () => logger.LogWarning("Rejected package operation with non-allow-listed package name"),
            "Package is not in the managed-package allow-list").ConfigureAwait(false);
        if (targetFailure is not null) return targetFailure;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            await onOutput("Package install/uninstall is only supported on Linux", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }

        // apt-get install/remove must run as root and legitimately execs dpkg + maintainer scripts, so
        // it goes through `sudo -n` (NOPASSWD) against the argv-exact AETHEUS_PACKAGE drop-in. The verb +
        // bare package name match the sudoers rule one-to-one; no shell, no string interpolation.
        var verb = kind == OperationKind.ServiceInstall ? "install" : "remove";
        timeoutSeconds = Math.Clamp(timeoutSeconds, _options.MinTimeoutSeconds, _options.MaxTimeoutSeconds);

        await AptLock.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Fresh-box guard: a newly-imaged server can ship an empty apt index, so `apt-get install`
            // would fail to locate the package. Refresh the index first (install only) - best-effort: a
            // failed update falls back to the cached index and does NOT fail the operation. `apt-get update`
            // is its own argv-exact entry in the AETHEUS_PACKAGE allow-list.
            if (kind == OperationKind.ServiceInstall)
            {
                var update = await AptProcessRunner.RunSudoAsync(
                    BuildAptUpdateArgv(), timeoutSeconds, onOutput, logger, cancellationToken).ConfigureAwait(false);
                if (update.ExitCode != 0)
                    await onOutput("apt-get update failed; continuing with the cached package index", TaskLogLevel.Warning).ConfigureAwait(false);
            }

            return await AptProcessRunner.RunSudoAsync(
                BuildAptArgv(verb, target), timeoutSeconds, onOutput, logger, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            AptLock.Gate.Release();
        }
    }

    // The full argv passed to `sudo` (unit-testable without spawning a process): `-n` (never prompt -
    // NOPASSWD is required), the absolute apt-get path, the verb, `-y`, and the bare package name -
    // all matching the AETHEUS_PACKAGE allow-list exactly.
    internal static IReadOnlyList<string> BuildAptArgv(string verb, string package) =>
        ["-n", "/usr/bin/apt-get", verb, "-y", package];

    // `sudo -n /usr/bin/apt-get update` - argv-exact, no args, matches the AETHEUS_PACKAGE allow-list.
    internal static IReadOnlyList<string> BuildAptUpdateArgv() =>
        ["-n", "/usr/bin/apt-get", "update"];
}
