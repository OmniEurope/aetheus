// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Front.Pages.Pipelines;

/// <summary>
/// Pure formatting / badge / colour / aggregation helpers for the run view. Extracted from the
/// former <c>PipelineRun.*.cs</c> partials into a real collaborator; the <c>PipelineRun.razor</c>
/// markup imports it via <c>@using static</c> so call sites stay unqualified.
/// </summary>
internal static class PipelineRunFormatting
{
    public static bool IsWindows(string? os) =>
        os?.Contains("Windows", StringComparison.OrdinalIgnoreCase) == true;

    public static string MatrixGroupKey(string stageName, string stepName) => $"{stageName}::{stepName}";

    public static TaskExecutionStatus GetAggregateStatus(List<PipelineStepRunDto> steps)
    {
        if (steps.Any(s => s.Status == TaskExecutionStatus.Failed)) return TaskExecutionStatus.Failed;
        if (steps.Any(s => s.Status == TaskExecutionStatus.Running)) return TaskExecutionStatus.Running;
        if (steps.All(s => s.Status == TaskExecutionStatus.Success)) return TaskExecutionStatus.Success;
        return TaskExecutionStatus.Pending;
    }

    public static string FormatDuration(DateTime? started, DateTime? completed)
    {
        if (started is null) return string.Empty;
        var end = completed ?? (started.Value.Kind == DateTimeKind.Utc ? DateTime.UtcNow : DateTime.Now);
        var duration = end - started.Value;
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
        return duration.TotalMinutes >= 1
            ? $"{(int)duration.TotalMinutes}m {duration.Seconds}s"
            : $"{duration.Seconds}s";
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
        >= 1024 => $"{bytes / 1024.0:F0} KB",
        _ => $"{bytes} B"
    };

    public static BadgeStyle GetRunBadge(PipelineStatus status) => PipelineHelper.GetRunBadge(status);

    public static BadgeStyle GetStepBadge(TaskExecutionStatus status) => status switch
    {
        TaskExecutionStatus.Success => BadgeStyle.Success,
        TaskExecutionStatus.Failed => BadgeStyle.Danger,
        TaskExecutionStatus.Running => BadgeStyle.Info,
        _ => BadgeStyle.Light
    };

    // RLBK: the deploy agent sets this output variable when a failed deploy's health gate triggered a
    // rollback that successfully restored the previous release - the step "failed" but prod is up again.
    public const string RolledBackVar = "DEPLOY_ROLLED_BACK";

    /// <summary>True when a failed deploy step was followed by a rollback that restored the previous
    /// release (so the app is active, not down) - surfaced as a distinct "Rolled back" state.</summary>
    public static bool IsRolledBack(PipelineStepRunDto step) =>
        step.Status == TaskExecutionStatus.Failed
        && step.OutputVariables.TryGetValue(RolledBackVar, out var v)
        && string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);

    // S-DES-18: a step that failed but was allowed to continue is a soft failure - show it orange
    // (warning) instead of red (danger) so it reads differently from a run-blocking failure. RLBK: a
    // rolled-back deploy is likewise a "warning, not down" state, distinct from a bare red failure.
    public static BadgeStyle GetStepBadge(PipelineStepRunDto step) =>
        IsRolledBack(step) || step is { Status: TaskExecutionStatus.Failed, ContinueOnError: true }
            ? BadgeStyle.Warning
            : GetStepBadge(step.Status);

    /// <summary>Status label for a step badge - "Rolled back" for a restored-after-failure deploy,
    /// otherwise the localized execution status.</summary>
    public static string StepStatusText(PipelineStepRunDto step, IStringLocalizer localizer) =>
        IsRolledBack(step) ? localizer["RolledBack"].Value : localizer.Localize(step.Status);

    // UCHN: cascade lineage read from a run's variables - the downstream-trigger handler forwards
    // UPSTREAM_PIPELINE (immediate parent name), UPSTREAM_RUN_ID (parent run, linkable) and
    // UPSTREAM_CHAIN (comma-separated ancestor pipeline ids). All null/0 for a directly-launched run.
    public static string? UpstreamPipeline(IReadOnlyDictionary<string, string> vars) =>
        vars.TryGetValue("UPSTREAM_PIPELINE", out var p) && p.Length > 0 ? p : null;

    public static int? UpstreamRunId(IReadOnlyDictionary<string, string> vars) =>
        vars.TryGetValue("UPSTREAM_RUN_ID", out var s) && int.TryParse(s, out var id) ? id : null;

    public static int UpstreamChainDepth(IReadOnlyDictionary<string, string> vars) =>
        vars.TryGetValue("UPSTREAM_CHAIN", out var c) && !string.IsNullOrWhiteSpace(c)
            ? c.Split(',', StringSplitOptions.RemoveEmptyEntries).Length
            : 0;

    public static string ShortSha(string commitHash) => commitHash[..Math.Min(8, commitHash.Length)];

    /// <summary>Middle-truncates an unbreakable identifier (a release named after a full commit sha,
    /// for instance) so it fits a fixed-width tile: <c>c-aad83…19fe4</c>. The caller keeps the full
    /// value on the element's title so nothing is lost.</summary>
    public static string TruncateMiddle(string value, int head = 7, int tail = 5)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= head + tail + 1) return value;
        return $"{value[..head]}…{value[^tail..]}";
    }

    // Every delivery pipeline stamps the built application version into APP_VERSION (see .pipeline/*.yaml),
    // so the run view can name the version a release actually shipped instead of only its release name.
    public const string AppVersionVar = "APP_VERSION";

    /// <summary>The application version this run built, read from the resolved <c>APP_VERSION</c>
    /// variable and falling back to the last step that emitted it as an output. Null when the run's
    /// pipeline does not stamp a version.</summary>
    public static string? BuiltAppVersion(PipelineRunDto run)
    {
        if (run.ResolvedVariables.TryGetValue(AppVersionVar, out var resolved) && !string.IsNullOrWhiteSpace(resolved))
            return resolved.Trim();
        var emitted = run.Steps
            .SelectMany(step => step.OutputVariables)
            .Where(output => string.Equals(output.Key, AppVersionVar, StringComparison.Ordinal)
                             && !string.IsNullOrWhiteSpace(output.Value))
            .Select(output => output.Value.Trim())
            .LastOrDefault();
        return emitted;
    }

    public static string FormatPercent(double rate) => $"{rate * 100:F1}%";

    public static string CoverageColor(double rate) => rate switch
    {
        >= 0.8 => "var(--rz-success)",
        >= 0.5 => "var(--rz-warning)",
        _ => "var(--rz-danger)"
    };

    public static string CoverageColorClass(double rate) => rate switch
    {
        >= 0.8 => "rz-color-success",
        >= 0.5 => "rz-color-warning",
        _ => "rz-color-danger"
    };

    // Average cyclomatic complexity: <=5 healthy, <=10 watch, higher is hot.
    public static string ComplexityColorClass(double avg) => avg switch
    {
        <= 5 => "rz-color-success",
        <= 10 => "rz-color-warning",
        _ => "rz-color-danger"
    };

    // CRAP: <=5 healthy, <=30 watch, higher is risky (hard-to-test complex code).
    public static string CrapColorClass(double crap) => crap switch
    {
        <= 5 => "rz-color-success",
        <= 30 => "rz-color-warning",
        _ => "rz-color-danger"
    };

    // Cobertura filenames are often long source paths; show the last two segments, full path on hover.
    public static string ShortPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length <= 2 ? path : string.Join('/', parts[^2..]);
    }

    public static bool IsWebUrl(string? url) =>
        !string.IsNullOrEmpty(url)
        && (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    public static string? BuildCommitUrl(string? repoUrl, string? sha)
    {
        if (!IsWebUrl(repoUrl) || string.IsNullOrEmpty(sha)) return null;
        var baseUrl = repoUrl!.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? repoUrl[..^4] : repoUrl;
        return $"{baseUrl.TrimEnd('/')}/commit/{sha}";
    }

    public static bool IsBlockingWarning(string warning) =>
        warning.Contains("no online server", StringComparison.OrdinalIgnoreCase)
        || warning.Contains("no online pipeline runner", StringComparison.OrdinalIgnoreCase)
        || warning.Contains("container isolation", StringComparison.OrdinalIgnoreCase)
        || warning.Contains("containers-only", StringComparison.OrdinalIgnoreCase);

    public static bool IsLintWarning(string message) =>
        message.Contains("warning", StringComparison.OrdinalIgnoreCase);

    // S-DES-22: true when two or more steps of a stage overlap in time (ran in parallel).
    public static bool StageRunsInParallel(StageViewModel stage)
    {
        var timed = stage.Steps.Where(s => s.StartedAt.HasValue).ToList();
        for (var i = 0; i < timed.Count; i++)
        {
            for (var j = i + 1; j < timed.Count; j++)
            {
                var aStart = timed[i].StartedAt!.Value;
                var aEnd = timed[i].CompletedAt ?? DateTime.Now;
                var bStart = timed[j].StartedAt!.Value;
                var bEnd = timed[j].CompletedAt ?? DateTime.Now;
                if (aStart < bEnd && bStart < aEnd) return true;
            }
        }
        return false;
    }

    private static readonly PipelineStatus[] TerminalRunStatuses =
        [PipelineStatus.Success, PipelineStatus.Failed, PipelineStatus.Cancelled];

    public static bool IsTerminal(PipelineStatus status) => TerminalRunStatuses.Contains(status);

    /// <summary>Orphaned / abnormally-finalized runs can reach a terminal status with a null
    /// <c>CompletedAt</c>. Derive the end from the last finished step so the run renders as the
    /// completed (cancelled/failed) run it actually is.</summary>
    public static PipelineRunDto NormalizeRunCompletion(PipelineRunDto run)
    {
        if (run.CompletedAt is not null || !TerminalRunStatuses.Contains(run.Status))
            return run;
        var lastStepEnd = run.Steps.Where(s => s.CompletedAt.HasValue).Select(s => s.CompletedAt).Max();
        return lastStepEnd is null ? run : run with { CompletedAt = lastStepEnd };
    }

    public static PointStyle StatusPointStyle(TaskExecutionStatus status) => status switch
    {
        TaskExecutionStatus.Success => PointStyle.Success,
        TaskExecutionStatus.Failed or TaskExecutionStatus.Timeout => PointStyle.Danger,
        TaskExecutionStatus.Cancelled => PointStyle.Warning,
        TaskExecutionStatus.Running => PointStyle.Info,
        _ => PointStyle.Light
    };

    public static string StatusPointIcon(TaskExecutionStatus status) => status switch
    {
        TaskExecutionStatus.Success => "check",
        TaskExecutionStatus.Failed or TaskExecutionStatus.Timeout => "close",
        TaskExecutionStatus.Cancelled => "block",
        TaskExecutionStatus.Running => "sync",
        _ => "schedule"
    };

    private static readonly Regex BlockingTargetRegex = new(
        @"no online server (?:matches agent|found in pool|found in environment) '(?<name>[^']+)'",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public sealed record BlockingWarningView(string Message, string? ConfigHref, string? ConfigName);

    public static BlockingWarningView ToBlockingView(string warning)
    {
        var match = BlockingTargetRegex.Match(warning);
        if (!match.Success) return new(warning, null, null);

        var href = warning.Contains("in environment", StringComparison.OrdinalIgnoreCase)
            ? "/environments"
            : "/servers";
        return new(warning, href, match.Groups["name"].Value);
    }
}
