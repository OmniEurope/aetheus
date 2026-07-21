// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using System.Runtime.InteropServices;
using Aetheus.Agent.Core.Executors;
using Aetheus.Shared.Constants;
using Aetheus.Shared.Enums;
using Aetheus.Shared.Validation;

namespace Aetheus.Agent.Core.Operations;

/// <summary>
/// PLAN-006 4.2: firewall (ufw) control via the root-owned <c>aetheus-firewall</c> helper. The helper is
/// the security boundary (root-owned, agent-non-writable, re-validates every argument AND re-enforces
/// anti-lockout with the real SSH port); the sudoers grant is path-only. This executor is defence in
/// depth: it re-validates port/protocol/source and refuses an anti-lockout deny BEFORE calling the helper.
/// Everything argv-only (<c>ProcessStartInfo.ArgumentList</c>) - no shell, no interpolation.
/// </summary>
public sealed class FirewallOperationExecutor(ILogger<FirewallOperationExecutor> logger) : IOperationExecutor
{
    public const string HelperPath = "/usr/local/lib/aetheus/aetheus-firewall";

    public bool CanHandle(OperationKind kind) => kind is
        OperationKind.FirewallAllow or
        OperationKind.FirewallDeny or
        OperationKind.FirewallDeleteRule or
        OperationKind.FirewallSetEnabled;

    public Task<ExecutorResult> ExecuteAsync(
        OperationKind kind, string target, int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput, CancellationToken cancellationToken)
        => ExecuteAsync(kind, target, new Dictionary<string, string>(), timeoutSeconds, onOutput, cancellationToken);

    public async Task<ExecutorResult> ExecuteAsync(
        OperationKind kind, string target, IReadOnlyDictionary<string, string> envVars,
        int timeoutSeconds, Func<string, TaskLogLevel, Task> onOutput, CancellationToken cancellationToken)
    {
        if (!OperationTargetValidator.IsValid(kind, target))
        {
            await onOutput("Invalid firewall operation target", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            await onOutput("Firewall management is only supported on Linux", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }

        IReadOnlyList<string> argv;
        if (kind == OperationKind.FirewallSetEnabled)
        {
            // target is "enable" | "disable" (validated). The helper auto-allows the admin port on enable.
            argv = ["-n", HelperPath, target];
        }
        else
        {
            var port = int.Parse(target);
            var proto = envVars.TryGetValue(FirewallConstants.ProtoEnvVar, out var pr) ? pr : "tcp";
            var source = envVars.TryGetValue(FirewallConstants.SourceEnvVar, out var src) && !string.IsNullOrWhiteSpace(src) ? src : "any";

            if (!FirewallValidation.IsValidProtocol(proto))
            {
                await onOutput($"Invalid protocol '{proto}' (allowed: tcp, udp)", TaskLogLevel.Error).ConfigureAwait(false);
                return new ExecutorResult(-1, false);
            }
            if (!FirewallValidation.IsValidSource(source))
            {
                await onOutput($"Invalid source '{source}' (expected a CIDR, an IP, or 'any')", TaskLogLevel.Error).ConfigureAwait(false);
                return new ExecutorResult(-1, false);
            }

            var adminPorts = BuildAdminPorts(envVars);
            if (FirewallLockoutGuard.IsLockoutRisk(kind, port, adminPorts))
            {
                await onOutput($"Refused: this would close the administration port {port} and lock you out.", TaskLogLevel.Error).ConfigureAwait(false);
                return new ExecutorResult(1, false);
            }

            var subcommand = kind switch
            {
                OperationKind.FirewallAllow => "allow",
                OperationKind.FirewallDeny => "deny",
                OperationKind.FirewallDeleteRule => "delete",
                _ => throw new InvalidOperationException($"Unhandled firewall kind {kind}")
            };
            argv = ["-n", HelperPath, subcommand, port.ToString(), proto, source];
        }

        return await RunSudoAsync(argv, timeoutSeconds, onOutput, cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlySet<int> BuildAdminPorts(IReadOnlyDictionary<string, string> envVars)
    {
        var ports = new HashSet<int>(FirewallLockoutGuard.DefaultAdminPorts);
        if (envVars.TryGetValue(FirewallConstants.AdminPortEnvVar, out var ap) && int.TryParse(ap, out var apInt))
            ports.Add(apInt);
        return ports;
    }

    private async Task<ExecutorResult> RunSudoAsync(
        IReadOnlyList<string> argv, int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "sudo",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in argv)
            psi.ArgumentList.Add(arg);

        return await ProcessRunner.RunAsync(psi, timeoutSeconds, onOutput, logger, cancellationToken).ConfigureAwait(false);
    }
}
