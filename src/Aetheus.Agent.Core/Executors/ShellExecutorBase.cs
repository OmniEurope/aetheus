// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;

namespace Aetheus.Agent.Core.Executors;

public abstract class ShellExecutorBase(
    ICommandValidator commandValidator,
    IOptions<AetheusAgentOptions> options,
    ExecutorProcessRunner processRunner,
    ILogger logger) : IExecutor
{
    protected AetheusAgentOptions Options { get; } = options.Value;
    protected ILogger Logger { get; } = logger;

    public ExecutorType Type => ExecutorType.Shell;

    public async Task<ExecutorResult> ExecuteAsync(
        string command,
        Dictionary<string, string> environmentVariables,
        int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken cancellationToken)
    {
        var rejection = await ValidateAsync(command, environmentVariables, onOutput).ConfigureAwait(false);
        if (rejection is not null)
            return rejection;

        timeoutSeconds = Math.Clamp(timeoutSeconds, Options.MinTimeoutSeconds, Options.MaxTimeoutSeconds);
        var startInfo = BuildProcessStartInfo(command);
        ConfigureEnvironment(startInfo, Options, environmentVariables);
        LogExecution(timeoutSeconds);

        return await processRunner.RunAsync(
            startInfo, timeoutSeconds, onOutput, cancellationToken).ConfigureAwait(false);
    }

    protected abstract ProcessStartInfo BuildProcessStartInfo(string command);

    protected abstract void LogExecution(int timeoutSeconds);

    private async Task<ExecutorResult?> ValidateAsync(
        string command,
        Dictionary<string, string> environmentVariables,
        Func<string, TaskLogLevel, Task> onOutput)
    {
        if (!commandValidator.IsAllowed(command))
        {
            var reason = commandValidator.GetRejectionReason(command) ?? "Unknown";
            Logger.LogWarning("Command blocked by validator: {Reason}", reason);
            await onOutput($"Command blocked by security policy - {reason}", TaskLogLevel.Error)
                .ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }

        var dangerousNames = commandValidator.GetDangerousEnvironmentVariableNames(environmentVariables);
        if (dangerousNames.Count == 0)
            return null;

        var names = string.Join(", ", dangerousNames);
        Logger.LogWarning("Dangerous environment variables detected: {Names}", names);
        await onOutput(
            $"Blocked dangerous environment variable(s): {names}. Rename application settings to a scoped name such as DEPLOY_ENV.",
            TaskLogLevel.Error).ConfigureAwait(false);
        return new ExecutorResult(-1, false);
    }

    internal static void ConfigureEnvironment(
        ProcessStartInfo startInfo,
        AetheusAgentOptions options,
        IReadOnlyDictionary<string, string> environmentVariables)
    {
        if (!string.IsNullOrWhiteSpace(options.WorkDirectory) && Directory.Exists(options.WorkDirectory))
            startInfo.WorkingDirectory = options.WorkDirectory;

        foreach (var (key, value) in environmentVariables)
            startInfo.Environment[key] = value;
    }
}
