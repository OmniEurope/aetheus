// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Shared;

internal static class PipelineRunTablePresentation
{
    public static string PipelineHref(PipelineRunTableItem run, int? serverId) => serverId.HasValue
        ? $"/pipelines/{run.PipelineId}?serverId={serverId}"
        : run.ProjectId.HasValue
            ? $"/pipelines/{run.PipelineId}?projectId={run.ProjectId}"
            : $"/pipelines/{run.PipelineId}";

    public static string RunHref(PipelineRunTableItem run, int? serverId) => serverId.HasValue
        ? $"/pipelines/runs/{run.RunId}?serverId={serverId}"
        : run.ProjectId.HasValue
            ? $"/pipelines/runs/{run.RunId}?projectId={run.ProjectId}"
            : $"/pipelines/runs/{run.RunId}";

    /// <summary>Link to the branch on the hosting web UI, or null when the run has no branch or the
    /// repository is not reachable over http(s) (a local path or an ssh remote has no web page).</summary>
    public static string? BranchHref(PipelineRunTableItem run)
    {
        if (string.IsNullOrWhiteSpace(run.BranchName) || !IsWebUrl(run.RepositoryUrl))
            return null;
        return $"{WebBase(run.RepositoryUrl!)}/tree/{Uri.EscapeDataString(run.BranchName)}";
    }

    /// <summary>Link to the commit on the hosting web UI, same reachability rule as the branch.</summary>
    public static string? CommitHref(PipelineRunTableItem run)
    {
        if (string.IsNullOrWhiteSpace(run.CommitHash) || !IsWebUrl(run.RepositoryUrl))
            return null;
        return $"{WebBase(run.RepositoryUrl!)}/commit/{Uri.EscapeDataString(run.CommitHash)}";
    }

    private static bool IsWebUrl(string? url) =>
        !string.IsNullOrEmpty(url)
        && (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    private static string WebBase(string repositoryUrl)
    {
        var baseUrl = repositoryUrl.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? repositoryUrl[..^4]
            : repositoryUrl;
        return baseUrl.TrimEnd('/');
    }

    public static string FormatDuration(
        PipelineRunTableItem run,
        IStringLocalizer<AppStrings> localizer) =>
        FormatDuration(run, localizer, DateTime.Now);

    public static string FormatDuration(
        PipelineRunTableItem run,
        IStringLocalizer<AppStrings> localizer,
        DateTime currentTime)
    {
        if (run.StartedAt.Year < 2000)
            return "-";

        var completedAt = run.CompletedAt;
        if (!completedAt.HasValue && run.Status != PipelineStatus.Running)
            return "-";

        var duration = (completedAt ?? currentTime) - run.StartedAt;
        if (duration < TimeSpan.Zero)
            return "-";
        if (duration.TotalDays >= 1)
            return string.Format(localizer["DurationDaysHoursFormat"], (int)duration.TotalDays, duration.Hours);
        if (duration.TotalHours >= 1)
            return string.Format(localizer["DurationHoursMinutesFormat"], (int)duration.TotalHours, duration.Minutes);
        if (duration.TotalMinutes >= 1)
            return string.Format(localizer["DurationMinutesSecondsFormat"], (int)duration.TotalMinutes, duration.Seconds);
        return string.Format(localizer["DurationSecondsFormat"], Math.Max(0, duration.Seconds));
    }
}
