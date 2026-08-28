// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Data.Entities;

public class AnalysisMetric
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int ProjectId { get; set; }
    public int AnalysisReportId { get; set; }
    public string Key { get; set; } = string.Empty;
    public double Value { get; set; }
    public string? Unit { get; set; }
    public string? Scope { get; set; }
    public string? Language { get; set; }
    public string? FilePath { get; set; }
    public string? Symbol { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public double? BaselineValue { get; set; }
    public AnalysisMetricDirection Direction { get; set; }
    public DateTime CreatedAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public Project Project { get; set; } = null!;
    public AnalysisReport AnalysisReport { get; set; } = null!;
}
