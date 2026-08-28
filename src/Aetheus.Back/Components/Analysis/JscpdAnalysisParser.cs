// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Aetheus.Back.Components.Analysis;

public sealed record ParsedJscpdReport(
    IReadOnlyList<ParsedAnalysisFinding> Findings,
    IReadOnlyList<ParsedAnalysisMetric> Metrics);

public static class JscpdAnalysisParser
{
    private const int MaxEntries = 100_000;

    public static ParsedJscpdReport Parse(string content)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 64 }); }
        catch (JsonException ex) { throw new BadRequestException($"Invalid jscpd report: {ex.Message}"); }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("statistics", out var statistics)
                || statistics.ValueKind != JsonValueKind.Object)
                throw new BadRequestException("Invalid jscpd report: statistics must be an object.");
            return new ParsedJscpdReport(ParseFindings(root), ParseMetrics(statistics));
        }
    }

    private static List<ParsedAnalysisMetric> ParseMetrics(JsonElement statistics)
    {
        var metrics = new List<ParsedAnalysisMetric>();
        if (statistics.TryGetProperty("total", out var total)) AddMetrics(total, null, metrics);
        if (statistics.TryGetProperty("formats", out var formats) && formats.ValueKind == JsonValueKind.Object)
            foreach (var format in formats.EnumerateObject()) AddMetrics(format.Value, format.Name, metrics);
        return metrics;
    }

    private static List<ParsedAnalysisFinding> ParseFindings(JsonElement root)
    {
        var findings = new List<ParsedAnalysisFinding>();
        if (!root.TryGetProperty("duplicates", out var duplicates) || duplicates.ValueKind != JsonValueKind.Array)
            return findings;
        foreach (var duplicate in duplicates.EnumerateArray())
        {
            if (findings.Count >= MaxEntries)
                throw new BadRequestException($"jscpd report exceeds the maximum of {MaxEntries} duplicates.");
            findings.Add(ParseFinding(duplicate));
        }
        return findings;
    }

    private static ParsedAnalysisFinding ParseFinding(JsonElement duplicate)
    {
        var first = ReadLocation(duplicate, "firstFile");
        var second = ReadLocation(duplicate, "secondFile");
        var format = AnalysisJsonValueReader.ReadString(duplicate, "format") ?? "unknown";
        var lines = ReadNumber(duplicate, "lines") ?? 0;
        var paths = new[] { first.Path ?? string.Empty, second.Path ?? string.Empty };
        Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
        var fingerprint = Hash(string.Join('|',
            ((int)AnalysisCategory.Duplication).ToString(CultureInfo.InvariantCulture),
            format.ToLowerInvariant(), paths[0].ToLowerInvariant(), paths[1].ToLowerInvariant()));
        return new ParsedAnalysisFinding
        {
            Fingerprint = fingerprint,
            LocationHash = Hash($"{fingerprint}|{first.Start ?? 0}|{second.Start ?? 0}"),
            ToolName = "jscpd",
            RuleId = "duplicate-block",
            Category = AnalysisCategory.Duplication,
            Severity = AnalysisSeverity.Low,
            Confidence = AnalysisConfidence.High,
            Title = "Duplicated code block",
            Message = $"A {lines.ToString(CultureInfo.InvariantCulture)}-line block is duplicated with {second.Path ?? "another file"}.",
            FilePath = first.Path,
            StartLine = first.Start,
            EndLine = first.End,
            Symbol = second.Path
        };
    }

    private static void AddMetrics(JsonElement item, string? language, List<ParsedAnalysisMetric> metrics)
    {
        AddMetric(item, "percentage", "duplication.percentage", "percent", language, metrics);
        AddMetric(item, "duplicatedLines", "duplication.lines", "lines", language, metrics);
        AddMetric(item, "clones", "duplication.clones", "count", language, metrics);
        AddMetric(item, "lines", "source.lines", "lines", language, metrics, AnalysisMetricDirection.Informational);
    }

    private static void AddMetric(
        JsonElement item,
        string property,
        string key,
        string unit,
        string? language,
        List<ParsedAnalysisMetric> metrics,
        AnalysisMetricDirection direction = AnalysisMetricDirection.LowerIsBetter)
    {
        if (metrics.Count >= MaxEntries)
            throw new BadRequestException($"jscpd report exceeds the maximum of {MaxEntries} metrics.");
        var value = ReadNumber(item, property);
        if (!value.HasValue || !double.IsFinite(value.Value)) return;
        metrics.Add(new ParsedAnalysisMetric(key, value.Value, unit, language is null ? "project" : "language",
            language, null, null, "jscpd", direction));
    }

    private static FileLocation ReadLocation(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var location) || location.ValueKind != JsonValueKind.Object)
            return new FileLocation(null, null, null);
        return new FileLocation(AnalysisJsonValueReader.NormalizePath(
                AnalysisJsonValueReader.ReadString(location, "name")),
            AnalysisJsonValueReader.ReadPositiveInt(location, "start"),
            AnalysisJsonValueReader.ReadPositiveInt(location, "end"));
    }

    private static double? ReadNumber(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            ? number : null;
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private sealed record FileLocation(string? Path, int? Start, int? End);
}
