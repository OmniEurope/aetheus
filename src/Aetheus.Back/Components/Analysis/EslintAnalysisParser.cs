// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Aetheus.Back.Components.Analysis;

public static class EslintAnalysisParser
{
    private const int MaxFindings = 100_000;

    public static IReadOnlyList<ParsedAnalysisFinding> Parse(string content)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 64 }); }
        catch (JsonException ex) { throw new BadRequestException($"Invalid ESLint report: {ex.Message}"); }
        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new BadRequestException("Invalid ESLint report: root must be an array.");
            var findings = new List<ParsedAnalysisFinding>();
            foreach (var file in document.RootElement.EnumerateArray())
                ParseFile(file, findings);
            return findings;
        }
    }

    private static void ParseFile(JsonElement file, List<ParsedAnalysisFinding> findings)
    {
        var path = AnalysisJsonValueReader.NormalizePath(
            AnalysisJsonValueReader.ReadString(file, "filePath"));
        if (!file.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array) return;
        foreach (var item in messages.EnumerateArray())
        {
            if (findings.Count >= MaxFindings)
                throw new BadRequestException($"ESLint report exceeds the maximum of {MaxFindings} findings.");
            findings.Add(ParseFinding(item, path));
        }
    }

    private static ParsedAnalysisFinding ParseFinding(JsonElement item, string? path)
    {
        var ruleId = AnalysisJsonValueReader.ReadString(item, "ruleId") ?? "eslint-parser";
        var message = Truncate(AnalysisJsonValueReader.ReadString(item, "message") ?? ruleId, 4000);
        var line = AnalysisJsonValueReader.ReadPositiveInt(item, "line");
        var endLine = AnalysisJsonValueReader.ReadPositiveInt(item, "endLine");
        var severity = AnalysisJsonValueReader.ReadPositiveInt(item, "severity") switch
        {
            2 => AnalysisSeverity.Medium,
            1 => AnalysisSeverity.Low,
            _ => AnalysisSeverity.Info
        };
        var fingerprint = Hash(string.Join('|',
            ((int)AnalysisCategory.CodeQuality).ToString(CultureInfo.InvariantCulture),
            ruleId.ToLowerInvariant(), path?.ToLowerInvariant() ?? string.Empty));
        return new ParsedAnalysisFinding
        {
            Fingerprint = fingerprint,
            LocationHash = Hash($"{fingerprint}|eslint|{line ?? 0}|{endLine ?? line ?? 0}"),
            ToolName = "ESLint",
            RuleId = Truncate(ruleId, 300),
            Category = AnalysisCategory.CodeQuality,
            Severity = severity,
            Confidence = AnalysisConfidence.High,
            Title = Truncate(ruleId, 500),
            Message = message,
            FilePath = path,
            StartLine = line,
            EndLine = endLine
        };
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
