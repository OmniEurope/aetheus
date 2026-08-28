// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;

namespace Aetheus.Back.Components.Analysis;

public sealed record ParsedAnalysisMetric(
    string Key,
    double Value,
    string? Unit,
    string? Scope,
    string? Language,
    string? FilePath,
    string? Symbol,
    string ToolName,
    AnalysisMetricDirection Direction);

public static class AnalysisMetricParser
{
    private const int MaxMetrics = 100_000;

    public static IReadOnlyList<ParsedAnalysisMetric> Parse(string content)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 32 }); }
        catch (JsonException ex) { throw new BadRequestException($"Invalid metrics report: {ex.Message}"); }
        using (document)
        {
            if (!document.RootElement.TryGetProperty("metrics", out var metrics) || metrics.ValueKind != JsonValueKind.Array)
                throw new BadRequestException("Invalid metrics report: metrics must be an array.");
            var parsed = new List<ParsedAnalysisMetric>();
            foreach (var metric in metrics.EnumerateArray())
            {
                if (parsed.Count >= MaxMetrics)
                    throw new BadRequestException($"Metrics report exceeds the maximum of {MaxMetrics} entries.");
                var key = Read(metric, "key");
                var tool = Read(metric, "toolName");
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(tool)
                    || !metric.TryGetProperty("value", out var valueElement)
                    || valueElement.ValueKind != JsonValueKind.Number
                    || !valueElement.TryGetDouble(out var value)
                    || !double.IsFinite(value))
                    throw new BadRequestException("Each metric requires a finite value, key and toolName.");
                var direction = Enum.TryParse<AnalysisMetricDirection>(Read(metric, "direction"), true, out var parsedDirection)
                    ? parsedDirection : AnalysisMetricDirection.Informational;
                parsed.Add(new ParsedAnalysisMetric(
                    Truncate(key, 300), value, TruncateNullable(Read(metric, "unit"), 50),
                    TruncateNullable(Read(metric, "scope"), 200), TruncateNullable(Read(metric, "language"), 100),
                    TruncateNullable(Read(metric, "filePath"), 1000), TruncateNullable(Read(metric, "symbol"), 500),
                    Truncate(tool, 200), direction));
            }
            return parsed;
        }
    }

    private static string? Read(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
    private static string? TruncateNullable(string? value, int max) => value is null ? null : Truncate(value, max);
}
