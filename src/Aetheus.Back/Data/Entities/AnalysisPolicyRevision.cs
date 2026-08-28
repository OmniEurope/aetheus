// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public sealed class AnalysisPolicyRevision
{
    public int Id { get; set; }
    public int AnalysisPolicyId { get; set; }
    public int Version { get; set; }
    public string SnapshotJson { get; set; } = string.Empty;
    public string SnapshotHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public AnalysisPolicy AnalysisPolicy { get; set; } = null!;
}
