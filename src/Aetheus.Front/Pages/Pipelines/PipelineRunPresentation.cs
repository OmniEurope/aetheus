// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Helpers;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Radzen;

namespace Aetheus.Front.Pages.Pipelines;

internal static class PipelineRunPresentation
{
    public static string EditBackHref(int? projectId, int? serverId) => projectId is { } project
        ? $"/projects/{project}/pipelines"
        : serverId is { } server ? $"/servers/{server}/pipelines" : "/pipelines";

    public static string Href(int runId, int? projectId, int? serverId) => projectId is { } project
        ? $"/pipelines/runs/{runId}?projectId={project}"
        : serverId is { } server ? $"/pipelines/runs/{runId}?serverId={server}" : $"/pipelines/runs/{runId}";

    public static string? CurrentStep(PipelineRunDto run) => PipelineHelper.GetCurrentStepLabel(run);
    public static bool IsFailed(PipelineRunDto run) => run.Status == PipelineStatus.Failed;
    public static bool IsConfigFailure(PipelineRunDto run) =>
        run.Status == PipelineStatus.Failed &&
        (run.Steps.Count == 0 || run.Steps.Any(step =>
            step.Status == TaskExecutionStatus.Failed && step.TaskId is null));
    public static bool IsExecutionFailure(PipelineRunDto run) =>
        run.Status == PipelineStatus.Failed && !IsConfigFailure(run);
    public static bool IsWindows(string? os) => os?.Contains("Windows", StringComparison.OrdinalIgnoreCase) == true;
    public static BadgeStyle Badge(PipelineStatus status) => PipelineHelper.GetRunBadge(status);

    public static List<string> TabSlugs(PipelineRunDto? run, bool hasYamlTab, bool hasLintTab)
    {
        var slugs = new List<string> { "overview", "logs" };
        if (hasYamlTab) slugs.Add("yaml");
        if (PipelineRunMetrics.HasCoverage(run)) slugs.Add("coverage");
        if (run?.TestResultSummary is not null) slugs.Add("tests");
        if (hasLintTab) slugs.Add("lint");
        if (PipelineRunMetrics.HasComplexity(run)) slugs.Add("quality");
        return slugs;
    }

    public static string NextConnectorStatus(IReadOnlyList<StageViewModel> stages, int stageIndex)
    {
        if (stageIndex + 1 >= stages.Count) return "pending";
        var current = stages[stageIndex];
        var next = stages[stageIndex + 1];
        if (current.Status == TaskExecutionStatus.Success && next.Status == TaskExecutionStatus.Failed)
            return "success-failed";
        if (current.Status == TaskExecutionStatus.Success && next.Status != TaskExecutionStatus.Pending)
            return next.Status.ToString().ToLowerInvariant();
        return current.Status.ToString().ToLowerInvariant();
    }

    public static string FormatDuration(DateTime started, DateTime? completed)
    {
        if (completed is null) return "\u2014";
        var duration = completed.Value - started;
        return duration.TotalMinutes >= 1
            ? $"{(int)duration.TotalMinutes}m {duration.Seconds}s"
            : $"{duration.Seconds}s";
    }
}
