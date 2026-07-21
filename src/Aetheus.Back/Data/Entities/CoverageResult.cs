// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class CoverageResult
{
    public int Id { get; set; }
    public int PipelineRunId { get; set; }
    public string? StageName { get; set; }
    public string? StepName { get; set; }
    public double LineRate { get; set; }
    public double BranchRate { get; set; }
    public int LinesCovered { get; set; }
    public int LinesValid { get; set; }
    public int BranchesCovered { get; set; }
    public int BranchesValid { get; set; }

    /// <summary>K: per-file coverage as a JSON array of {File, LineRate, LinesCovered, LinesValid},
    /// parsed from the Cobertura report's classes. Null for legacy rows captured before per-file support.</summary>
    public string? FilesJson { get; set; }

    public DateTime CreatedAt { get; set; }

    // Navigation
    public PipelineRun PipelineRun { get; set; } = null!;
}
