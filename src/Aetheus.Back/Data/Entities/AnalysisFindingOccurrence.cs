// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class AnalysisFindingOccurrence
{
    public int Id { get; set; }
    public int AnalysisReportId { get; set; }
    public int AnalysisFindingId { get; set; }
    public string LocationHash { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public string ScannerKey { get; set; } = string.Empty;
    public string RuleId { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public int? StartLine { get; set; }
    public int? EndLine { get; set; }
    public string? Symbol { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? BranchName { get; set; }
    public string? CommitHash { get; set; }
    public bool IsNew { get; set; }
    public DateTime CreatedAt { get; set; }

    public AnalysisReport AnalysisReport { get; set; } = null!;
    public AnalysisFinding AnalysisFinding { get; set; } = null!;
}
