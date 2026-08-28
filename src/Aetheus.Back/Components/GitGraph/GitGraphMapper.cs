// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.GitGraph;

/// <summary>
/// Maps cross-linking entities to their lightweight link DTOs. Shared by the release, artifact
/// and git-graph services so every page renders identical cross-link tiles.
/// </summary>
internal static class GitGraphMapper
{
    public static ReleaseLinkDto ToLink(Release r) => new()
    {
        Id = r.Id,
        Version = r.Version,
        Status = r.Status
    };

    public static ArtifactLinkDto ToLink(PipelineArtifact a) => new()
    {
        Id = a.Id,
        Name = a.Name,
        SizeBytes = a.SizeBytes,
        RetentionPolicy = a.RetentionPolicy
    };

    public static CommitLinkDto ToLink(GitCommit c) => new()
    {
        Id = c.Id,
        Sha = c.Sha,
        Message = c.Message
    };

    public static BranchLinkDto ToLink(GitBranch b) => new()
    {
        Id = b.Id,
        Name = b.Name
    };
}
