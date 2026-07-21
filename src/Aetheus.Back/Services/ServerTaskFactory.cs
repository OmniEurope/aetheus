// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Services;

/// <summary>
/// Centralised factory for <see cref="ServerTask"/> instances. Removes the
/// dozens of duplicated object-initializer blocks scattered across module
/// services and gives a single place to evolve task creation defaults.
/// </summary>
public static class ServerTaskFactory
{
    /// <summary>Creates a <c>Shell</c> task in the <c>Pending</c> state.</summary>
    public static ServerTask Shell(int serverId, string name, string command, int timeoutSeconds = 60)
        => new()
        {
            ServerId = serverId,
            Name = name,
            Command = command,
            Executor = ExecutorType.Shell,
            Status = TaskExecutionStatus.Pending,
            TimeoutSeconds = timeoutSeconds
        };

    /// <summary>Creates a task with an explicit executor.</summary>
    public static ServerTask Create(int serverId, string name, string command, ExecutorType executor, int timeoutSeconds = 60)
        => new()
        {
            ServerId = serverId,
            Name = name,
            Command = command,
            Executor = executor,
            Status = TaskExecutionStatus.Pending,
            TimeoutSeconds = timeoutSeconds
        };

    /// <summary>
    /// Creates a typed-operation task (no shell, no allow-list - the agent's
    /// <c>IOperationExecutor</c> dispatches by <see cref="OperationKind"/>). The
    /// <paramref name="target"/> string is validated by
    /// <c>OperationTargetValidator</c> at both the controller and the agent.
    /// </summary>
    public static ServerTask Operation(int serverId, string name, OperationKind operation, string target, int timeoutSeconds = 60)
        => new()
        {
            ServerId = serverId,
            Name = name,
            Command = target,                 // target carried in Command for parity with shell tasks
            Executor = ExecutorType.Operation,
            Operation = operation,
            Status = TaskExecutionStatus.Pending,
            TimeoutSeconds = timeoutSeconds,
            EnvironmentVariables = "{}"
        };

    /// <summary>
    /// Creates a typed-operation task carrying additional non-secret env vars (e.g. the cron
    /// user/schedule/command for <see cref="OperationKind.CronSave"/>). The dictionary is serialized
    /// as-is; secret-bearing tasks must use the protected-env path in their own service instead.
    /// </summary>
    public static ServerTask Operation(int serverId, string name, OperationKind operation, string target,
        IReadOnlyDictionary<string, string> environmentVariables, int timeoutSeconds = 60)
        => new()
        {
            ServerId = serverId,
            Name = name,
            Command = target,
            Executor = ExecutorType.Operation,
            Operation = operation,
            Status = TaskExecutionStatus.Pending,
            TimeoutSeconds = timeoutSeconds,
            EnvironmentVariables = JsonSerializer.Serialize(environmentVariables)
        };
}
