// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.Enums;

namespace Aetheus.Shared.DTOs;

// --- R-01: Git Connection ---

public sealed record GitConnectionDto
{
    public int Id { get; init; }
    public int ProjectId { get; init; }
    public string? ProjectName { get; init; }
    public GitProviderType ProviderType { get; init; }
    public string OwnerOrGroup { get; init; } = string.Empty;
    public string RepositoryName { get; init; } = string.Empty;
    public string? ServiceConnectionName { get; init; }
    public bool AutoSyncEnabled { get; init; }
    public DateTime? LastSyncedAt { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed record CreateGitConnectionRequest
{
    public int ProjectId { get; init; }

    public GitProviderType ProviderType { get; init; }

    [Required]
    [StringLength(200)]
    public string OwnerOrGroup { get; init; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string RepositoryName { get; init; } = string.Empty;

    public int? ServiceConnectionId { get; init; }
    public bool AutoSyncEnabled { get; init; } = true;
}

public sealed record UpdateGitConnectionRequest
{
    public int? ServiceConnectionId { get; init; }
    public bool AutoSyncEnabled { get; init; } = true;
}

// --- R-02: Pull Requests ---

public sealed record PullRequestDto
{
    public int Id { get; init; }
    public int GitConnectionId { get; init; }
    public int ExternalId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string SourceBranch { get; init; } = string.Empty;
    public string TargetBranch { get; init; } = string.Empty;
    public string AuthorLogin { get; init; } = string.Empty;
    public PullRequestStatus Status { get; init; }
    public string? ExternalUrl { get; init; }
    public string? HeadCommitSha { get; init; }
    public int? LinkedPipelineRunId { get; init; }
    public DateTime ExternalCreatedAt { get; init; }
    public DateTime? ExternalMergedAt { get; init; }
    public DateTime LastSyncedAt { get; init; }
}

public sealed record PullRequestPaginationRequest : PaginationRequest
{
    public PullRequestStatus? Status { get; init; }
}

// --- R-03: Branch Policies ---

public sealed record BranchPolicyDto
{
    public int Id { get; init; }
    public int GitConnectionId { get; init; }
    public string BranchPattern { get; init; } = string.Empty;
    public BranchPolicyType PolicyType { get; init; }
    public string? ConfigurationJson { get; init; }
    public bool IsEnabled { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed record CreateBranchPolicyRequest
{
    public int GitConnectionId { get; init; }

    [Required]
    [StringLength(300)]
    public string BranchPattern { get; init; } = string.Empty;

    public BranchPolicyType PolicyType { get; init; }

    [StringLength(2000)]
    public string? ConfigurationJson { get; init; }

    public bool IsEnabled { get; init; } = true;
}

public sealed record UpdateBranchPolicyRequest
{
    [Required]
    [StringLength(300)]
    public string BranchPattern { get; init; } = string.Empty;

    public BranchPolicyType PolicyType { get; init; }

    [StringLength(2000)]
    public string? ConfigurationJson { get; init; }

    public bool IsEnabled { get; init; } = true;
}

// --- P-43: PR Status Reporting ---

public sealed record PipelineStatusReport
{
    public int PipelineRunId { get; init; }
    public string State { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? TargetUrl { get; init; }
    public string Context { get; init; } = "aetheus-ci";
}
