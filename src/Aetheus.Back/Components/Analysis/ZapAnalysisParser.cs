// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Aetheus.Back.Components.Analysis;

public static class ZapAnalysisParser
{
    private const int MaxFindings = 100_000;

    public static IReadOnlyList<ParsedAnalysisFinding> Parse(string content)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 64 });
        }
        catch (JsonException ex)
        {
            throw new BadRequestException($"Invalid ZAP report: {ex.Message}");
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("site", out var sites) || sites.ValueKind != JsonValueKind.Array)
                throw new BadRequestException("Invalid ZAP report: site must be an array.");
            var findings = new List<ParsedAnalysisFinding>();
            ParseSites(sites, findings);
            return findings;
        }
    }

    private static void ParseSites(JsonElement sites, List<ParsedAnalysisFinding> findings)
    {
        foreach (var site in sites.EnumerateArray())
        {
            if (!site.TryGetProperty("alerts", out var alerts) || alerts.ValueKind != JsonValueKind.Array) continue;
            foreach (var alert in alerts.EnumerateArray()) ParseAlert(alert, findings);
        }
    }

    private static void ParseAlert(JsonElement alert, List<ParsedAnalysisFinding> findings)
    {
        var riskCode = Read(alert, "riskcode");
        if (riskCode is null or "0") return;
        var ruleId = Read(alert, "pluginid") ?? "unknown";
        var title = Truncate(Read(alert, "alert") ?? ruleId, 500);
        var message = Truncate(StripMarkup(Read(alert, "desc") ?? title), 4000);
        var cweId = Read(alert, "cweid");
        var cwe = cweId is null or "-1" or "0" ? null : $"CWE-{cweId}";
        var descriptor = new AlertDescriptor(
            ruleId, title, message, cwe, ParseSeverity(riskCode),
            ParseConfidence(Read(alert, "confidence")), FirstUrl(Read(alert, "reference")));
        if (!alert.TryGetProperty("instances", out var instances) || instances.ValueKind != JsonValueKind.Array)
        {
            AddDescriptor(descriptor, null, null, findings);
            return;
        }
        foreach (var instance in instances.EnumerateArray()) ParseInstance(descriptor, instance, findings);
    }

    private static void ParseInstance(
        AlertDescriptor alert,
        JsonElement instance,
        List<ParsedAnalysisFinding> findings)
    {
        var uriText = Read(instance, "uri");
        if (alert.RuleId == "10105" && IsLoopbackHttp(uriText)) return;
        AddDescriptor(alert, uriText,
            $"{Read(instance, "method") ?? "HTTP"} {Read(instance, "param")}".Trim(), findings);
    }

    private static void AddDescriptor(
        AlertDescriptor alert,
        string? uriText,
        string? symbol,
        List<ParsedAnalysisFinding> findings) => Add(
        alert.RuleId, alert.Title, alert.Message, alert.Cwe, alert.Severity,
        alert.Confidence, alert.HelpUri, uriText, symbol, findings);

    private static void Add(
        string ruleId,
        string title,
        string message,
        string? cwe,
        AnalysisSeverity severity,
        AnalysisConfidence confidence,
        string? helpUri,
        string? uriText,
        string? symbol,
        List<ParsedAnalysisFinding> findings)
    {
        if (findings.Count >= MaxFindings)
            throw new BadRequestException($"ZAP report exceeds the maximum of {MaxFindings} findings.");
        var path = Uri.TryCreate(uriText, UriKind.Absolute, out var uri) ? uri.AbsolutePath : uriText;
        var fingerprint = Hash(string.Join('|',
            ((int)AnalysisCategory.Dast).ToString(CultureInfo.InvariantCulture),
            (cwe ?? ruleId).ToLowerInvariant(),
            (path ?? string.Empty).ToLowerInvariant(),
            (symbol ?? string.Empty).ToLowerInvariant()));
        findings.Add(new ParsedAnalysisFinding
        {
            Fingerprint = fingerprint,
            LocationHash = Hash($"{fingerprint}|zap"),
            ToolName = "OWASP ZAP",
            RuleId = Truncate(ruleId, 300),
            Category = AnalysisCategory.Dast,
            Severity = severity,
            Confidence = confidence,
            Cwe = cwe,
            Title = title,
            Message = message,
            HelpUri = helpUri,
            FilePath = TruncateNullable(path, 1000),
            Symbol = TruncateNullable(symbol, 500)
        });
    }

    private static string? Read(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static AnalysisSeverity ParseSeverity(string? value) => value switch
    {
        "4" => AnalysisSeverity.Critical,
        "3" => AnalysisSeverity.High,
        "2" => AnalysisSeverity.Medium,
        "1" => AnalysisSeverity.Low,
        _ => AnalysisSeverity.Info
    };

    private static AnalysisConfidence ParseConfidence(string? value) => value switch
    {
        "3" or "4" => AnalysisConfidence.High,
        "2" => AnalysisConfidence.Medium,
        "1" => AnalysisConfidence.Low,
        _ => AnalysisConfidence.Unknown
    };

    private static string StripMarkup(string value) => value
        .Replace("<p>", string.Empty, StringComparison.OrdinalIgnoreCase)
        .Replace("</p>", " ", StringComparison.OrdinalIgnoreCase)
        .Replace("<br>", " ", StringComparison.OrdinalIgnoreCase)
        .Replace("<br/>", " ", StringComparison.OrdinalIgnoreCase);

    private static string? FirstUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Split(['\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(part => Uri.TryCreate(part, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https");
    }

    private static bool IsLoopbackHttp(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttp
        && uri.IsLoopback;

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];
    private static string? TruncateNullable(string? value, int length) => value is null ? null : Truncate(value, length);

    private sealed record AlertDescriptor(
        string RuleId,
        string Title,
        string Message,
        string? Cwe,
        AnalysisSeverity Severity,
        AnalysisConfidence Confidence,
        string? HelpUri);
}
