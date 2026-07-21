// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Radzen;

namespace Aetheus.Front.Helpers;

internal static class PipelineHelper
{
    internal static BadgeStyle GetRunBadge(PipelineStatus status) => status switch
    {
        PipelineStatus.Success => BadgeStyle.Success,
        PipelineStatus.Failed => BadgeStyle.Danger,
        PipelineStatus.Running => BadgeStyle.Info,
        PipelineStatus.Cancelled => BadgeStyle.Warning,
        _ => BadgeStyle.Light
    };

    /// <summary>M: the step a non-terminal run is currently on, as "Stage · Step" (the running step,
    /// else the next pending non-system step). Null for terminal runs (the status badge conveys the
    /// outcome) or when no step data is loaded. The "System:" stage prefix is stripped for display.</summary>
    internal static string? GetCurrentStepLabel(PipelineRunDto run)
    {
        if (run.Status is not (PipelineStatus.Running or PipelineStatus.Pending or PipelineStatus.WaitingForApproval)) return null;

        var step = run.Steps.FirstOrDefault(s => s.Status == TaskExecutionStatus.Running)
                   ?? run.Steps.FirstOrDefault(s => s.Status == TaskExecutionStatus.Assigned)
                   ?? run.Steps.FirstOrDefault(s => s.Status == TaskExecutionStatus.Pending && !s.IsSystem);
        if (step is null) return null;

        var stage = step.StageName.StartsWith("System:", StringComparison.Ordinal)
            ? step.StageName["System:".Length..]
            : step.StageName;
        return $"{stage} · {step.StepName}";
    }

    internal static string GetTriggerIcon(PipelineTriggerType type) => type switch
    {
        PipelineTriggerType.Webhook => "webhook",
        PipelineTriggerType.Schedule => "schedule",
        _ => "touch_app"
    };
}
