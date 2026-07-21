// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using System.Runtime.InteropServices;
using Aetheus.Agent.Core.Configuration;
using Aetheus.Agent.Core.Executors;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Options;

namespace Aetheus.Agent.Core.Operations;

/// <summary>
/// Items #10.2/#10.3 of the plan - typed RKHunter operations under the same controlled-sudo
/// recipe Apache uses (item #6). Each kind maps 1:1 to a line in
/// <c>/etc/sudoers.d/aetheus-rkhunter</c> dropped by
/// <c>install-agent-linux.sh --enable-rkhunter-manage</c>. Argv is exact - no flags from the
/// caller, no shell, no metacharacters.
/// </summary>
public sealed class RkhunterOperationExecutor(
    IOptions<AetheusAgentOptions> options,
    ILogger<RkhunterOperationExecutor> logger) : IOperationExecutor
{
    private readonly AetheusAgentOptions _options = options.Value;

    public bool CanHandle(OperationKind kind) => kind is
        OperationKind.RkhunterScan or
        OperationKind.RkhunterUpdate or
        OperationKind.RkhunterPropupd;

    public async Task<ExecutorResult> ExecuteAsync(
        OperationKind kind,
        string target,
        int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken cancellationToken)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            await onOutput("RKHunter operations are only supported on Linux", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }

        timeoutSeconds = Math.Clamp(timeoutSeconds, _options.MinTimeoutSeconds, _options.MaxTimeoutSeconds);

        var psi = new ProcessStartInfo
        {
            FileName = "sudo",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var argv = BuildSudoArgv(kind);
        if (argv is null) return new ExecutorResult(-1, false);
        foreach (var arg in argv) psi.ArgumentList.Add(arg);

        return await ProcessRunner.RunAsync(psi, timeoutSeconds, onOutput, logger, cancellationToken).ConfigureAwait(false);
    }

    // Exact `sudo -n /usr/bin/rkhunter ...` argv per kind (unit-testable without spawning). No
    // caller-supplied flags - each maps 1:1 to a line in /etc/sudoers.d/aetheus-rkhunter.
    internal static string[]? BuildSudoArgv(OperationKind kind) => kind switch
    {
        // --report-warnings-only keeps the stdout focused on actionable findings, critical
        // when the front-side surface only displays a short summary.
        OperationKind.RkhunterScan => ["-n", "/usr/bin/rkhunter", "--check", "--skip-keypress", "--nocolors", "--report-warnings-only"],
        OperationKind.RkhunterUpdate => ["-n", "/usr/bin/rkhunter", "--update", "--nocolors"],
        OperationKind.RkhunterPropupd => ["-n", "/usr/bin/rkhunter", "--propupd", "--nocolors"],
        _ => null
    };
}
