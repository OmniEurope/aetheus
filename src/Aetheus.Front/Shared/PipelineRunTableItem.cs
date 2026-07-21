// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Helpers;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;

namespace Aetheus.Front.Shared;

public sealed record PipelineRunTableItem
{
    public int RunId { get; init; }
    public int PipelineId { get; init; }
    public string PipelineName { get; init; } = string.Empty;
    public int? ProjectId { get; init; }
    public string? ProjectName { get; init; }
    public PipelineStatus Status { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? ServerName { get; init; }
    public string? ServerOs { get; init; }
    public string? CurrentStep { get; init; }

    public static PipelineRunTableItem FromRun(PipelineRunDto run) => new()
    {
        RunId = run.Id,
        PipelineId = run.PipelineId,
        PipelineName = run.PipelineName,
        ProjectId = run.ProjectId,
        ProjectName = run.ProjectName,
        Status = run.Status,
        StartedAt = run.StartedAt,
        CompletedAt = run.CompletedAt,
        ServerName = run.Steps.FirstOrDefault(step => !step.IsSystem && step.ServerId.HasValue)?.ServerName,
        ServerOs = run.Steps.FirstOrDefault(step => !step.IsSystem && step.ServerId.HasValue)?.ServerOs,
        CurrentStep = PipelineHelper.GetCurrentStepLabel(run)
    };
}
