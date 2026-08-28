// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;
using System.Text.RegularExpressions;
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Tasks;

internal static partial class TaskPipelineMetricParser
{
    private const int MaxMetricsPerTask = 256;

    [GeneratedRegex(
        @"##aetheus\[pipelinemetric key=([a-z0-9][a-z0-9._-]{0,127});type=(Percentage|Count|Score|Duration|Size);unit=([^\]\r\n]{0,16})\]([^\r\n]+)$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex MetricPattern();

    internal static List<RunMetric> Parse(
        string? output,
        int pipelineRunId,
        string? stageName,
        string? stepName,
        DateTime createdAt)
    {
        var metrics = new List<RunMetric>();
        if (string.IsNullOrEmpty(output)) return metrics;

        foreach (Match match in MetricPattern().Matches(output))
        {
            if (metrics.Count == MaxMetricsPerTask) break;
            if (!Enum.TryParse<RunMetricType>(match.Groups[2].Value, ignoreCase: true, out var type)
                || !double.TryParse(
                    match.Groups[4].Value.Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value)
                || !double.IsFinite(value))
                continue;

            var unit = match.Groups[3].Value.Trim();
            metrics.Add(new RunMetric
            {
                PipelineRunId = pipelineRunId,
                StageName = stageName,
                StepName = stepName,
                Key = match.Groups[1].Value.ToLowerInvariant(),
                Type = type,
                Value = value,
                Unit = string.IsNullOrEmpty(unit) ? null : unit,
                CreatedAt = createdAt
            });
        }

        return metrics;
    }
}
