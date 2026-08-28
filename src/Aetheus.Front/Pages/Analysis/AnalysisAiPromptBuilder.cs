// SPDX-License-Identifier: EUPL-1.2
using System.Text;

namespace Aetheus.Front.Pages.Analysis;

internal static class AnalysisAiPromptBuilder
{
    public static string BuildSingle(AnalysisFindingDto finding, IStringLocalizer<AppStrings> localizer) =>
        BuildSingle(PromptFinding.From(finding), localizer);

    public static string BuildBulk(
        IEnumerable<AnalysisFindingDto> findings,
        IStringLocalizer<AppStrings> localizer) =>
        BuildBulk(findings.Select(PromptFinding.From), localizer);

    public static string BuildBulk(
        IEnumerable<AnalysisRunGateFindingDto> findings,
        IStringLocalizer<AppStrings> localizer) =>
        BuildBulk(findings.Select(PromptFinding.From), localizer);

    private static string BuildSingle(PromptFinding finding, IStringLocalizer<AppStrings> localizer)
    {
        var prompt = new StringBuilder()
            .AppendLine($"# {localizer["AnalysisAiPromptTitle"]}: {finding.Title}")
            .AppendLine();
        AppendContext(prompt, finding, localizer, "##");
        prompt.AppendLine()
            .AppendLine($"## {localizer["AnalysisAiPromptEvidence"]}")
            .AppendLine(finding.Message)
            .AppendLine()
            .AppendLine($"## {localizer["AnalysisAiPromptInstructions"]}")
            .AppendLine(localizer["AnalysisAiPromptInstructionBody"]);
        return prompt.ToString();
    }

    private static string BuildBulk(
        IEnumerable<PromptFinding> findings,
        IStringLocalizer<AppStrings> localizer)
    {
        var items = findings.ToArray();
        var prompt = new StringBuilder()
            .AppendLine($"# {localizer["AnalysisBulkAiPromptTitle"]}")
            .AppendLine()
            .AppendLine($"{localizer["AnalysisBulkAiPromptCount"]}: {items.Length}");

        foreach (var finding in items)
        {
            prompt.AppendLine()
                .AppendLine($"## #{finding.Id} {finding.Title}")
                .AppendLine();
            AppendContext(prompt, finding, localizer, "###");
            prompt.AppendLine()
                .AppendLine($"### {localizer["AnalysisAiPromptEvidence"]}")
                .AppendLine(finding.Message);
        }

        prompt.AppendLine()
            .AppendLine($"## {localizer["AnalysisAiPromptInstructions"]}")
            .AppendLine(localizer["AnalysisAiPromptInstructionBody"]);
        return prompt.ToString();
    }

    private static void AppendContext(
        StringBuilder prompt,
        PromptFinding finding,
        IStringLocalizer<AppStrings> localizer,
        string heading)
    {
        prompt.AppendLine($"{heading} {localizer["AnalysisAiPromptContext"]}")
            .AppendLine($"- {localizer["Severity"]}: {localizer[$"AnalysisSeverity{finding.Severity}"]}")
            .AppendLine($"- {localizer["Category"]}: {localizer[$"QualityGateCategory{finding.Category}"]}")
            .AppendLine($"- {localizer["Status"]}: {localizer[$"AnalysisFindingStatus{finding.Status}"]}")
            .AppendLine($"- {localizer["AnalysisRule"]}: {finding.RuleId}")
            .AppendLine($"- CWE: {finding.Cwe ?? localizer["NotAvailable"]}")
            .AppendLine($"- {localizer["File"]}: {finding.FilePath ?? localizer["NotAvailable"]}")
            .AppendLine($"- {localizer["Line"]}: {finding.StartLine?.ToString() ?? localizer["NotAvailable"]}")
            .AppendLine($"- {localizer["Symbol"]}: {finding.Symbol ?? localizer["NotAvailable"]}");
    }

    private sealed record PromptFinding(
        int Id,
        string RuleId,
        string Title,
        string Message,
        AnalysisCategory Category,
        AnalysisSeverity Severity,
        AnalysisFindingStatus Status,
        string? Cwe,
        string? FilePath,
        int? StartLine,
        string? Symbol)
    {
        public static PromptFinding From(AnalysisFindingDto finding) => new(
            finding.Id,
            finding.RuleId,
            finding.Title,
            finding.LatestOccurrence?.Message ?? finding.Message,
            finding.Category,
            finding.Severity,
            finding.Status,
            finding.Cwe,
            finding.LatestOccurrence?.FilePath,
            finding.LatestOccurrence?.StartLine,
            finding.LatestOccurrence?.Symbol);

        public static PromptFinding From(AnalysisRunGateFindingDto finding) => new(
            finding.FindingId,
            finding.RuleId,
            finding.Title,
            finding.Message,
            finding.Category,
            finding.Severity,
            finding.Status,
            null,
            finding.FilePath,
            finding.StartLine,
            null);
    }
}
