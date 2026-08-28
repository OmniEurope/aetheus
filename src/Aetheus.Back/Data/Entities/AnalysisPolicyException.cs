// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Data.Entities;

public class AnalysisPolicyException
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int ProjectId { get; set; }
    public int? AnalysisPolicyId { get; set; }
    public int? AnalysisFindingId { get; set; }
    public string? Fingerprint { get; set; }
    public string? RuleId { get; set; }
    public string? ScannerKey { get; set; }
    public AnalysisCategory? Category { get; set; }
    public string? BranchPattern { get; set; }
    public string? EnvironmentPattern { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string CreatedByUsername { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime? ExpirationNotificationSentAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public Project Project { get; set; } = null!;
    public AnalysisPolicy? AnalysisPolicy { get; set; }
    public AnalysisFinding? AnalysisFinding { get; set; }
}
