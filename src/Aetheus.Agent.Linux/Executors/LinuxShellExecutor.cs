// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using Aetheus.Agent.Core.Configuration;
using Aetheus.Agent.Core.Executors;
using Microsoft.Extensions.Options;

namespace Aetheus.Agent.Linux.Executors;

public sealed class LinuxShellExecutor(
    ICommandValidator commandValidator,
    IOptions<AetheusAgentOptions> options,
    ExecutorProcessRunner processRunner,
    ILogger<LinuxShellExecutor> logger)
    : ShellExecutorBase(commandValidator, options, processRunner, logger)
{
    protected override ProcessStartInfo BuildProcessStartInfo(string command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-c");
        // Phase 3 hardening: tighten the file-creation mask (0077 - new files are owner-only, so a
        // step can't leave world/group-readable artifacts in shared dirs) and cap the largest file a
        // step may write (RLIMIT_FSIZE, ~4 GiB) as a cheap disk-fill backstop. Both are best-effort
        // (`|| true`) so a shell that lacks ulimit support still runs. The command is appended as a
        // single -c argument, so this preamble runs in the same shell before the user script.
        psi.ArgumentList.Add("umask 0077\nulimit -f 4194304 2>/dev/null || true\n" + command);
        return psi;
    }

    protected override void LogExecution(int timeoutSeconds) =>
        Logger.LogDebug("Executing shell command (timeout={Timeout}s)", timeoutSeconds);
}
