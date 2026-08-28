// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Data.Entities;

public class PullRequest
{
    public int Id { get; set; }
    public int GitConnectionId { get; set; }
    public int ExternalId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string SourceBranch { get; set; } = string.Empty;
    public string TargetBranch { get; set; } = string.Empty;
    public string AuthorLogin { get; set; } = string.Empty;
    public PullRequestStatus Status { get; set; } = PullRequestStatus.Open;
    public string? ExternalUrl { get; set; }
    public string? HeadCommitSha { get; set; }
    public string? MergeCommitSha { get; set; }
    public int? LinkedPipelineRunId { get; set; }
    public DateTime ExternalCreatedAt { get; set; }
    public DateTime? ExternalMergedAt { get; set; }
    public DateTime LastSyncedAt { get; set; }

    // Navigation
    public GitConnection GitConnection { get; set; } = null!;
    public PipelineRun? LinkedPipelineRun { get; set; }
}
