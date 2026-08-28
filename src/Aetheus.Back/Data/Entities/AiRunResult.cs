// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Data.Entities;

public sealed class AiRunResult
{
    public int Id { get; set; }
    public int ServerTaskId { get; set; }
    public int? PipelineRunId { get; set; }
    public int? AiTaskDefinitionId { get; set; }
    public string ProfileName { get; set; } = string.Empty;
    public bool SendsDataExternally { get; set; }
    public string ReportMarkdown { get; set; } = string.Empty;
    public AiVerdict Verdict { get; set; }
    public string? DiffPatch { get; set; }
    public long DurationMs { get; set; }
    public bool Truncated { get; set; }
    public bool Succeeded { get; set; }
    public int? SourceRepositoryId { get; set; }
    public string? BaseCommitSha { get; set; }
    public int? ProposedRepositoryId { get; set; }
    public string? ProposedBranchName { get; set; }
    public string? ProposedCommitSha { get; set; }
    public DateTime CreatedAt { get; set; }

    public ServerTask ServerTask { get; set; } = null!;
    public PipelineRun? PipelineRun { get; set; }
    public AiTaskDefinition? AiTaskDefinition { get; set; }
}
