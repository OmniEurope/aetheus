// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Analysis;

public sealed record ParsedAnalysisFinding
{
    public string Fingerprint { get; init; } = string.Empty;
    public string LocationHash { get; init; } = string.Empty;
    public string ToolName { get; init; } = string.Empty;
    public string RuleId { get; init; } = string.Empty;
    public AnalysisCategory Category { get; init; }
    public AnalysisSeverity Severity { get; init; }
    public AnalysisConfidence Confidence { get; init; }
    public string? Cwe { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? HelpUri { get; init; }
    public string? FilePath { get; init; }
    public int? StartLine { get; init; }
    public int? EndLine { get; init; }
    public string? Symbol { get; init; }
}
