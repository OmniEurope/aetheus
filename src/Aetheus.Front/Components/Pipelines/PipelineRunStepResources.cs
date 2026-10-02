// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Pipelines;

/// <summary>
/// What a step actually consumed on its runner, from the <c>step.*</c> metrics the agent already
/// publishes. They were stored and never shown, so the question "can these two stages run at once on
/// this box" had no answer anywhere in the UI.
///
/// Aggregated per stage the only way that means anything: CPU and disk add up, peak memory does not.
/// Two steps each peaking at 2 GB did not need 4 GB unless they ran at the same time, and summing
/// peaks would state exactly the thing a reader would use to size a runner.
/// </summary>
internal static class PipelineRunStepResources
{
    private const string CpuKey = "step.cpu";
    private const string MemoryKey = "step.memory.peak";
    private const string DiskReadKey = "step.disk.read";
    private const string DiskWrittenKey = "step.disk.written";

    internal sealed record Usage(
        double? CpuSeconds,
        double? PeakMemoryBytes,
        double? DiskBytes)
    {
        public bool IsEmpty => CpuSeconds is null && PeakMemoryBytes is null && DiskBytes is null;
    }

    /// <summary>The one step's usage, or null when the agent published none (an older agent, a step
    /// that is not a host process, or a step still running).</summary>
    public static Usage? ForStep(PipelineRunDto? run, PipelineStepRunDto step)
    {
        if (run is null) return null;
        var metrics = run.Metrics
            .Where(metric => Matches(metric, step.StageName, step.StepName))
            .ToList();
        return Build(metrics);
    }

    /// <summary>Every step of one stage, summed where summing is meaningful.</summary>
    public static Usage? ForStage(PipelineRunDto? run, string stageName)
    {
        if (run is null) return null;
        var metrics = run.Metrics
            .Where(metric => string.Equals(metric.StageName, stageName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return Build(metrics);
    }

    private static bool Matches(RunMetricDto metric, string stageName, string stepName) =>
        string.Equals(metric.StageName, stageName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(metric.StepName, stepName, StringComparison.OrdinalIgnoreCase);

    private static Usage? Build(List<RunMetricDto> metrics)
    {
        if (metrics.Count == 0) return null;

        double? Sum(string key)
        {
            var matching = metrics.Where(metric => metric.Key == key).ToList();
            return matching.Count == 0 ? null : matching.Sum(metric => metric.Value);
        }

        double? Max(string key)
        {
            var matching = metrics.Where(metric => metric.Key == key).ToList();
            return matching.Count == 0 ? null : matching.Max(metric => metric.Value);
        }

        var disk = new[] { Sum(DiskReadKey), Sum(DiskWrittenKey) }.OfType<double>().ToList();
        var usage = new Usage(
            Sum(CpuKey),
            Max(MemoryKey),
            disk.Count == 0 ? null : disk.Sum());
        return usage.IsEmpty ? null : usage;
    }

    /// <summary>The usage as one short line, e.g. <c>cpu 12s · 480 MB · io 1.2 GB</c>. Only the parts
    /// the agent actually published appear, rather than zeros standing in for missing measurements.</summary>
    public static string Describe(Usage usage)
    {
        var parts = new List<string>();
        if (usage.CpuSeconds is { } cpu)
            parts.Add("cpu " + PipelineRunFormatting.FormatDuration(
                DateTime.UnixEpoch, DateTime.UnixEpoch + TimeSpan.FromSeconds(cpu)));
        if (usage.PeakMemoryBytes is { } memory)
            parts.Add(PipelineRunFormatting.FormatSize((long)memory));
        if (usage.DiskBytes is { } diskBytes and > 0)
            parts.Add("io " + PipelineRunFormatting.FormatSize((long)diskBytes));
        return string.Join(" · ", parts);
    }
}
