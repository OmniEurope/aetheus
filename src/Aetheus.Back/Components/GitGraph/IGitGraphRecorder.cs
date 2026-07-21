// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Components.GitGraph;

/// <summary>
/// Records git provenance into the cross-link graph as it happens, so commits/branches are no
/// longer populated solely by the one-shot migration backfill. Best-effort by contract: a failure
/// (e.g. a unique-index race) must never break the run-trigger / release-create primary flow.
/// </summary>
public interface IGitGraphRecorder
{
    /// <summary>
    /// Get-or-create the run's commit and branch for the project (and link the commit to the
    /// branch), so the run-detail view can resolve them to internal git pages.
    /// </summary>
    Task RecordRunContextAsync(int projectId, string? sha, string? branchName, CancellationToken ct = default);

    /// <summary>
    /// Get-or-create the release's commit and branch and link both to the (tracked) release.
    /// </summary>
    Task RecordReleaseContextAsync(Release release, CancellationToken ct = default);

    /// <summary>
    /// Resolve the run's commit/branch strings to the internal git-graph link DTOs, or empty lists
    /// when no graph node exists yet. Read-only - never mutates the graph.
    /// </summary>
    Task<(List<CommitLinkDto> Commits, List<BranchLinkDto> Branches)> ResolveRunLinksAsync(
        int projectId, string? sha, string? branchName, CancellationToken ct = default);
}
