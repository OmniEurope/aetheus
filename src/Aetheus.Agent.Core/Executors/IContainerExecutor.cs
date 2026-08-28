// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Executors;

/// <summary>
/// Runs a pipeline step's shell script inside an ephemeral, hardened container. Separate from
/// <see cref="IExecutor"/> because it needs the <see cref="ContainerSpec"/> (image / runtime /
/// network / workspace key) that a plain shell step doesn't carry.
/// </summary>
public interface IContainerExecutor
{
    Task<ExecutorResult> ExecuteAsync(
        ContainerSpec spec,
        string command,
        Dictionary<string, string> environmentVariables,
        int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken cancellationToken);
}
