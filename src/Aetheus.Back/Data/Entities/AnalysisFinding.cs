// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Data.Entities;

public class AnalysisFinding
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int ProjectId { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public int FingerprintVersion { get; set; } = 1;
    public string RuleId { get; set; } = string.Empty;
    public AnalysisCategory Category { get; set; }
    public AnalysisSeverity Severity { get; set; }
    public AnalysisConfidence Confidence { get; set; }
    public string? Cwe { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? HelpUri { get; set; }
    public AnalysisFindingStatus Status { get; set; } = AnalysisFindingStatus.Open;
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public Project Project { get; set; } = null!;
    public List<AnalysisFindingOccurrence> Occurrences { get; set; } = [];
    public List<AnalysisFindingDecision> Decisions { get; set; } = [];
}
