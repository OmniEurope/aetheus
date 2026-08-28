// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;

namespace Aetheus.Back.Components.Analysis;

internal static class AnalysisGateViolationParser
{
    public static IReadOnlyList<AnalysisRunGateViolationDto> Parse(
        IEnumerable<AnalysisEvaluationSnapshot> evaluations)
    {
        var violations = new List<AnalysisRunGateViolationDto>();
        foreach (var evaluation in evaluations)
        {
            try
            {
                using var document = JsonDocument.Parse(evaluation.SnapshotJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("Aggregates", out var aggregates)
                    || aggregates.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var aggregate in aggregates.EnumerateArray())
                {
                    var outcome = Text(aggregate, "Outcome");
                    if (outcome is not ("Block" or "Warn")) continue;
                    if (!TryParseSource(Text(aggregate, "Source"), out var key, out var scope, out var version))
                        continue;
                    violations.Add(new AnalysisRunGateViolationDto
                    {
                        ReportId = evaluation.ReportId,
                        PolicyKey = key,
                        Scope = scope,
                        Version = version,
                        Outcome = outcome,
                        TargetKind = Text(aggregate, "TargetKind") ?? string.Empty,
                        TargetKey = Text(aggregate, "TargetKey"),
                        ObservedValue = Text(aggregate, "ObservedValue"),
                        Operator = Text(aggregate, "Operator"),
                        Threshold = Text(aggregate, "Threshold"),
                        Count = Number(aggregate, "Count"),
                        PolicySnapshotHash = Text(aggregate, "PolicySnapshotHash")
                    });
                }
            }
            catch (JsonException)
            {
                // Historical or malformed evidence cannot be treated as an actionable violation.
            }
        }

        return violations
            .OrderBy(item => item.Outcome == "Warn")
            .ThenBy(item => item.PolicyKey, StringComparer.Ordinal)
            .ThenBy(item => item.ReportId)
            .Take(500)
            .ToList();
    }

    private static bool TryParseSource(
        string? source,
        out string key,
        out AnalysisPolicyScope scope,
        out int version)
    {
        key = string.Empty;
        scope = default;
        version = 0;
        var parts = source?.Split(':');
        if (parts is not { Length: 4 }
            || parts[0] != "policy"
            || !Enum.TryParse(parts[2], out scope)
            || parts[3].Length < 2
            || parts[3][0] != 'v'
            || !int.TryParse(parts[3].AsSpan(1), out version))
            return false;
        key = parts[1];
        return key.Length > 0;
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int Number(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var number)
            ? number
            : 0;
}

internal sealed record AnalysisEvaluationSnapshot(
    int ReportId,
    string SnapshotJson,
    string GradeSnapshotJson = "{}");
