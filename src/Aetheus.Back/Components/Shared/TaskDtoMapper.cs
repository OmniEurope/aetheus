// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Shared;

internal static class TaskDtoMapper
{
    public static ServerTaskDto ToServerDetailDto(ServerTask task) =>
        Map(task, task.Command, string.Empty, ServerStatus.Online);

    public static ServerTaskDto ToMaskedDto(ServerTask task) =>
        Map(
            task,
            "[masked]",
            task.Server?.Name ?? string.Empty,
            task.Server?.Status ?? ServerStatus.Online);

    private static ServerTaskDto Map(
        ServerTask task,
        string command,
        string serverName,
        ServerStatus serverStatus) => new()
        {
            Id = task.Id,
            ServerId = task.ServerId,
            ServerName = serverName,
            ServerStatus = serverStatus,
            Name = task.Name,
            Command = command,
            Executor = task.Executor,
            Status = task.Status,
            PipelineRunId = task.PipelineRunId,
            PipelineStepRunId = task.PipelineStepRunId,
            CreatedAt = task.CreatedAt,
            StartedAt = task.StartedAt,
            CompletedAt = task.CompletedAt,
            ExitCode = task.ExitCode,
            FailureCode = task.FailureCode,
            FailureReason = task.FailureReason,
            TimeoutSeconds = task.TimeoutSeconds
        };
}
