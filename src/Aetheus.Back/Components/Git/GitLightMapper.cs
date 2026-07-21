// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Components.Git;

/// <summary>
/// Entity → DTO mappers for <see cref="GitLightService"/>, extracted to a real collaborator
/// (not a <c>partial</c> split) to keep the service within the per-file size budget.
/// <c>partial</c> here is required only for the <c>[GeneratedRegex]</c> source generator.
/// </summary>
internal static partial class GitLightMapper
{
    // Clone URL is resolved by the service (it depends on injected configuration), so it is
    // passed in rather than computed here.
    public static GitLightRepoDto MapRepoToDto(GitInternalRepo r, string cloneUrl) => new()
    {
        Id = r.Id,
        ProjectId = r.ProjectId,
        ProjectName = r.Project?.Name,
        Name = r.Name,
        Slug = r.Slug,
        Description = r.Description,
        DefaultBranch = r.DefaultBranch,
        IsEmpty = r.IsEmpty,
        CloneUrl = cloneUrl,
        CreatedAt = r.CreatedAt,
        LastPushAt = r.LastPushAt
    };

    public static InternalPullRequestDto MapPrToDto(PullRequest p) => new()
    {
        Id = p.Id,
        Number = p.ExternalId,
        Title = p.Title,
        Description = p.Description,
        SourceBranch = p.SourceBranch,
        TargetBranch = p.TargetBranch,
        AuthorLogin = p.AuthorLogin,
        Status = p.Status,
        HeadCommitSha = p.HeadCommitSha,
        MergeCommitSha = p.MergeCommitSha,
        CreatedAt = p.ExternalCreatedAt,
        MergedAt = p.ExternalMergedAt
    };

    public static string GenerateSlug(string name)
    {
        var slug = name.ToLowerInvariant().Trim();
        slug = SlugInvalidChars().Replace(slug, "-");
        slug = SlugMultipleDashes().Replace(slug, "-");
        return slug.Trim('-');
    }

    public static BranchProtectionRuleDto MapProtectionRuleToDto(BranchProtectionRule r) => new()
    {
        Id = r.Id,
        GitInternalRepoId = r.GitInternalRepoId,
        Pattern = r.Pattern,
        PreventDeletion = r.PreventDeletion,
        PreventForcePush = r.PreventForcePush,
        RequirePullRequest = r.RequirePullRequest,
        CreatedAt = r.CreatedAt
    };

    [GeneratedRegex(@"[^a-z0-9\-]")]
    private static partial Regex SlugInvalidChars();

    [GeneratedRegex(@"-{2,}")]
    private static partial Regex SlugMultipleDashes();
}
