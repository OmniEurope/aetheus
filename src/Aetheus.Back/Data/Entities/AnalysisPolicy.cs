// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Data.Entities;

public class AnalysisPolicy
{
    public int Id { get; set; }
    public int? OrganizationId { get; set; }
    public int? ProjectId { get; set; }
    public string PolicyKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AnalysisCategory? Category { get; set; }
    public string? ScannerKey { get; set; }
    public string? RuleId { get; set; }
    public AnalysisSeverity? SeverityThreshold { get; set; }
    public bool NewFindingsOnly { get; set; }
    public string? MetricKey { get; set; }
    public AnalysisPolicyOperator? Operator { get; set; }
    public double? Threshold { get; set; }
    public string? BranchPattern { get; set; }
    public string? EnvironmentPattern { get; set; }
    public AnalysisGateBehavior Behavior { get; set; }
    public int Priority { get; set; }
    public bool Enabled { get; set; } = true;
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Organization? Organization { get; set; }
    public Project? Project { get; set; }
    public List<AnalysisPolicyException> Exceptions { get; set; } = [];
    public List<AnalysisPolicyRevision> Revisions { get; set; } = [];
}
