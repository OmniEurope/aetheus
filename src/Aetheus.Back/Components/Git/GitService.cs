// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Logging;

namespace Aetheus.Back.Components.Git;

public class GitService(IGitRepository repo, IPipelineRepository pipelineRepo, IAuditService audit, ILogger<GitService> logger, TimeProvider timeProvider) : IGitService
{
    public async Task<List<GitConnectionDto>> GetConnectionsByProjectAsync(int projectId, CancellationToken ct = default)
    {
        var connections = await repo.GetConnectionsByProjectAsync(projectId, ct).ConfigureAwait(false);
        return connections.Select(MapConnectionToDto).ToList();
    }

    public async Task<GitConnectionDto?> GetConnectionDetailAsync(int id, CancellationToken ct = default)
    {
        var connection = await repo.GetConnectionDetailAsync(id, ct).ConfigureAwait(false);
        return connection is null ? null : MapConnectionToDto(connection);
    }

    public async Task<GitConnectionDto> CreateConnectionAsync(CreateGitConnectionRequest request, CancellationToken ct = default)
    {
        var entity = new GitConnection
        {
            ProjectId = request.ProjectId,
            ProviderType = request.ProviderType,
            OwnerOrGroup = request.OwnerOrGroup,
            RepositoryName = request.RepositoryName,
            ServiceConnectionId = request.ServiceConnectionId,
            AutoSyncEnabled = request.AutoSyncEnabled
        };

        await repo.AddConnectionAsync(entity, ct).ConfigureAwait(false);
        await audit.LogAsync("Created", "GitConnection", entity.Id, $"{request.ProviderType}: {request.OwnerOrGroup}/{request.RepositoryName}", ct).ConfigureAwait(false);

        return MapConnectionToDto(entity);
    }

    public async Task<GitConnectionDto?> UpdateConnectionAsync(int id, UpdateGitConnectionRequest request, CancellationToken ct = default)
    {
        var entity = await repo.FindConnectionAsync(id, ct).ConfigureAwait(false);
        if (entity is null) return null;

        entity.ServiceConnectionId = request.ServiceConnectionId;
        entity.AutoSyncEnabled = request.AutoSyncEnabled;
        entity.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.LogAsync("Updated", "GitConnection", id, null, ct).ConfigureAwait(false);

        return MapConnectionToDto(entity);
    }

    public async Task<bool> DeleteConnectionAsync(int id, CancellationToken ct = default)
    {
        var entity = await repo.FindConnectionAsync(id, ct).ConfigureAwait(false);
        if (entity is null) return false;

        await repo.RemoveConnectionAsync(entity, ct).ConfigureAwait(false);
        await audit.LogAsync("Deleted", "GitConnection", id, null, ct).ConfigureAwait(false);
        return true;
    }

    public async Task<PaginatedResult<PullRequestDto>> GetPullRequestsAsync(
        int gitConnectionId, PullRequestPaginationRequest request, CancellationToken ct = default)
    {
        var (page, pageSize) = request.Normalize();
        var (items, totalCount) = await repo.GetPullRequestsPagedAsync(
            gitConnectionId, request.Search, page, pageSize, request.Status, ct).ConfigureAwait(false);

        return new PaginatedResult<PullRequestDto>
        {
            Items = items.Select(MapPullRequestToDto).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<PullRequestDto?> SyncPullRequestAsync(int gitConnectionId, int externalId, PullRequestDto incoming, CancellationToken ct = default)
    {
        var existing = await repo.FindPullRequestByExternalIdAsync(gitConnectionId, externalId, ct).ConfigureAwait(false);

        if (existing is not null)
        {
            existing.Title = incoming.Title;
            existing.Description = incoming.Description;
            existing.Status = incoming.Status;
            existing.HeadCommitSha = incoming.HeadCommitSha;
            existing.ExternalMergedAt = incoming.ExternalMergedAt;
            existing.LastSyncedAt = timeProvider.GetUtcNow().UtcDateTime;
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
            return MapPullRequestToDto(existing);
        }

        var entity = new PullRequest
        {
            GitConnectionId = gitConnectionId,
            ExternalId = externalId,
            Title = incoming.Title,
            Description = incoming.Description,
            SourceBranch = incoming.SourceBranch,
            TargetBranch = incoming.TargetBranch,
            AuthorLogin = incoming.AuthorLogin,
            Status = incoming.Status,
            ExternalUrl = incoming.ExternalUrl,
            HeadCommitSha = incoming.HeadCommitSha,
            ExternalCreatedAt = incoming.ExternalCreatedAt,
            ExternalMergedAt = incoming.ExternalMergedAt
        };

        await repo.AddPullRequestAsync(entity, ct).ConfigureAwait(false);
        return MapPullRequestToDto(entity);
    }

    public async Task<List<BranchPolicyDto>> GetBranchPoliciesAsync(int gitConnectionId, CancellationToken ct = default)
    {
        var policies = await repo.GetBranchPoliciesAsync(gitConnectionId, ct).ConfigureAwait(false);
        return policies.Select(MapBranchPolicyToDto).ToList();
    }

    public async Task<BranchPolicyDto> CreateBranchPolicyAsync(CreateBranchPolicyRequest request, CancellationToken ct = default)
    {
        var entity = new BranchPolicy
        {
            GitConnectionId = request.GitConnectionId,
            BranchPattern = request.BranchPattern,
            PolicyType = request.PolicyType,
            ConfigurationJson = request.ConfigurationJson,
            IsEnabled = request.IsEnabled
        };

        await repo.AddBranchPolicyAsync(entity, ct).ConfigureAwait(false);
        await audit.LogAsync("Created", "BranchPolicy", entity.Id, $"{request.PolicyType}: {request.BranchPattern}", ct).ConfigureAwait(false);
        return MapBranchPolicyToDto(entity);
    }

    public async Task<BranchPolicyDto?> UpdateBranchPolicyAsync(int id, UpdateBranchPolicyRequest request, CancellationToken ct = default)
    {
        var entity = await repo.FindBranchPolicyAsync(id, ct).ConfigureAwait(false);
        if (entity is null) return null;

        entity.BranchPattern = request.BranchPattern;
        entity.PolicyType = request.PolicyType;
        entity.ConfigurationJson = request.ConfigurationJson;
        entity.IsEnabled = request.IsEnabled;
        entity.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.LogAsync("Updated", "BranchPolicy", id, null, ct).ConfigureAwait(false);
        return MapBranchPolicyToDto(entity);
    }

    public async Task<bool> DeleteBranchPolicyAsync(int id, CancellationToken ct = default)
    {
        var entity = await repo.FindBranchPolicyAsync(id, ct).ConfigureAwait(false);
        if (entity is null) return false;

        await repo.RemoveBranchPolicyAsync(entity, ct).ConfigureAwait(false);
        await audit.LogAsync("Deleted", "BranchPolicy", id, null, ct).ConfigureAwait(false);
        return true;
    }

    public Task ReportPipelineStatusAsync(PipelineStatusReport report, CancellationToken ct = default)
    {
        // P-43: records the status report locally (structured log). Forwarding to the provider's
        // commit-status API (GitHub/GitLab) is tracked functional debt and NOT yet wired - the caller
        // is told so via the controller's { forwardedToProvider = false } body, so no green is faked.
        logger.LogInformation(
            "Pipeline status recorded (local-only, not forwarded to provider) for run {RunId}: {State} ({Context})",
            report.PipelineRunId, report.State, report.Context);
        return Task.CompletedTask;
    }

    public async Task<int?> GetProjectIdForConnectionAsync(int connectionId, CancellationToken ct = default)
    {
        var c = await repo.FindConnectionAsync(connectionId, ct).ConfigureAwait(false);
        return c?.ProjectId;
    }

    public async Task<int?> GetProjectIdForBranchPolicyAsync(int branchPolicyId, CancellationToken ct = default)
    {
        var bp = await repo.FindBranchPolicyAsync(branchPolicyId, ct).ConfigureAwait(false);
        if (bp is null) return null;
        var c = await repo.FindConnectionAsync(bp.GitConnectionId, ct).ConfigureAwait(false);
        return c?.ProjectId;
    }

    public async Task<int?> GetProjectIdForRunAsync(int pipelineRunId, CancellationToken ct = default)
    {
        var run = await pipelineRepo.GetPipelineRunWithPipelineAsync(pipelineRunId, ct).ConfigureAwait(false);
        return run?.Pipeline?.ProjectId;
    }

    private static GitConnectionDto MapConnectionToDto(GitConnection g) => new()
    {
        Id = g.Id,
        ProjectId = g.ProjectId,
        ProjectName = g.Project?.Name,
        ProviderType = g.ProviderType,
        OwnerOrGroup = g.OwnerOrGroup,
        RepositoryName = g.RepositoryName,
        ServiceConnectionName = g.ServiceConnection?.Name,
        AutoSyncEnabled = g.AutoSyncEnabled,
        LastSyncedAt = g.LastSyncedAt,
        CreatedAt = g.CreatedAt
    };

    private static PullRequestDto MapPullRequestToDto(PullRequest p) => new()
    {
        Id = p.Id,
        GitConnectionId = p.GitConnectionId,
        ExternalId = p.ExternalId,
        Title = p.Title,
        Description = p.Description,
        SourceBranch = p.SourceBranch,
        TargetBranch = p.TargetBranch,
        AuthorLogin = p.AuthorLogin,
        Status = p.Status,
        ExternalUrl = p.ExternalUrl,
        HeadCommitSha = p.HeadCommitSha,
        LinkedPipelineRunId = p.LinkedPipelineRunId,
        ExternalCreatedAt = p.ExternalCreatedAt,
        ExternalMergedAt = p.ExternalMergedAt,
        LastSyncedAt = p.LastSyncedAt
    };

    private static BranchPolicyDto MapBranchPolicyToDto(BranchPolicy b) => new()
    {
        Id = b.Id,
        GitConnectionId = b.GitConnectionId,
        BranchPattern = b.BranchPattern,
        PolicyType = b.PolicyType,
        ConfigurationJson = b.ConfigurationJson,
        IsEnabled = b.IsEnabled,
        CreatedAt = b.CreatedAt
    };
}
