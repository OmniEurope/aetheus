// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using Aetheus.Agent.Core.Configuration;
using Aetheus.Agent.Core.Executors;
using Microsoft.Extensions.Options;

namespace Aetheus.Agent.Windows.Executors;

public sealed class WindowsShellExecutor(
    ICommandValidator commandValidator,
    IOptions<AetheusAgentOptions> options,
    ExecutorProcessRunner processRunner,
    ILogger<WindowsShellExecutor> logger)
    : ShellExecutorBase(commandValidator, options, processRunner, logger)
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);
    private readonly Lazy<string> _cachedShell = new(() => DetectPowerShell(logger));

    private static string DetectPowerShell(ILogger logger)
    {
        foreach (var candidate in new[] { "pwsh", "powershell" })
        {
            try
            {
                var psi = new ProcessStartInfo(candidate, "-NoLogo -NonInteractive -Command \"exit 0\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var process = Process.Start(psi);
                if (process is null)
                {
                    logger.LogWarning("PowerShell probe {Candidate} could not be started", candidate);
                    continue;
                }
                if (!process.WaitForExit((int)ProbeTimeout.TotalMilliseconds))
                {
                    logger.LogWarning(
                        "PowerShell probe {Candidate} exceeded {TimeoutSeconds}s and will be terminated",
                        candidate,
                        ProbeTimeout.TotalSeconds);
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit();
                    continue;
                }
                if (process.ExitCode == 0) return candidate;
                logger.LogDebug(
                    "PowerShell probe {Candidate} exited with code {ExitCode}",
                    candidate,
                    process.ExitCode);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
                or InvalidOperationException
                or NotSupportedException)
            {
                logger.LogDebug(ex, "PowerShell probe {Candidate} is unavailable", candidate);
            }
        }
        logger.LogWarning("No PowerShell probe succeeded; falling back to powershell");
        return "powershell";
    }

    protected override ProcessStartInfo BuildProcessStartInfo(string command)
    {
        var shell = _cachedShell.Value;
        var psi = new ProcessStartInfo
        {
            FileName = shell,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-NoLogo");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(command);
        return psi;
    }

    protected override void LogExecution(int timeoutSeconds) =>
        Logger.LogDebug("Executing {Shell} command (timeout={Timeout}s)", _cachedShell.Value, timeoutSeconds);
}
