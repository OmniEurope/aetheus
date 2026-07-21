// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Enums;

namespace Aetheus.Agent.Core.Executors;

public interface IExecutor
{
    ExecutorType Type { get; }

    Task<ExecutorResult> ExecuteAsync(
        string command,
        Dictionary<string, string> environmentVariables,
        int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken cancellationToken);
}

public sealed record ExecutorResult(int ExitCode, bool TimedOut);
