// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics.CodeAnalysis;
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Releases;

/// <summary>What the end of a run does to the release that points at it.</summary>
internal static class ReleaseRunOutcome
{
    /// <summary>
    /// Applies <paramref name="status"/> to <paramref name="release"/>; returns <c>false</c> when the
    /// release is left as it was. A release already deployed stays deployed. R-10: a run that ends
    /// badly decides only for a release it was still building - deploy-prod 2367 failed at TLS,
    /// before production changed, and turned the published candidate it was deploying into a Failed
    /// release the launch dialog then hid. The payload did not change; the attempt failed, and that
    /// stays visible as the status of the run the release points at.
    /// </summary>
    /// <summary>
    /// True when a run records <paramref name="existing"/> as not deployed although an earlier run
    /// deployed it. A rollback records its version that way to undo its own run's deployment; a
    /// release an earlier run deployed is what the rollback returns to - deploy-prod 2369 redeployed
    /// the live one - and is left as it is.
    /// </summary>
    public static bool IsLiveBeforeThisRun([NotNullWhen(true)] Release? existing, bool deployed, int pipelineRunId) =>
        existing is { Status: ReleaseStatus.Deployed } && !deployed && existing.PipelineRunId != pipelineRunId;

    /// <summary>
    /// Recette R2-001: true when a run must leave the commit <paramref name="existing"/> records as it
    /// is. A run that records the release as deployed, or demotes a deployed one (the rollback of
    /// <c>aetheus-deploy-prod</c>), did not build it: its own workspace head may be a newer commit that
    /// was never deployed, and a branch advanced onto the release would then name that commit.
    /// </summary>
    public static bool KeepsRecordedCommit(Release existing, bool deployed) =>
        !string.IsNullOrWhiteSpace(existing.CommitHash)
        && (deployed || existing.Status == ReleaseStatus.Deployed);

    /// <summary>
    /// Writes the commit, tag and branch a run reports onto a release it records again; a value the
    /// run does not send leaves the recorded one. The commit follows <see cref="KeepsRecordedCommit"/>,
    /// so this runs before the release's status changes.
    /// </summary>
    public static void RecordSourceIdentity(
        Release existing, bool deployed, string? commitHash, string? tagName, string? branchName)
    {
        if (commitHash is not null && !KeepsRecordedCommit(existing, deployed)) existing.CommitHash = commitHash;
        if (tagName is not null) existing.TagName = tagName;
        if (branchName is not null) existing.BranchName = branchName;
    }

    public static bool Apply(Release release, PipelineStatus status, DateTime nowUtc)
    {
        if (status != PipelineStatus.Success && release.Status is not (ReleaseStatus.Detected or ReleaseStatus.Building))
            return false;

        release.Status = release.Status == ReleaseStatus.Deployed
            ? ReleaseStatus.Deployed
            : status == PipelineStatus.Success ? ReleaseStatus.Published : ReleaseStatus.Failed;
        if (release.Status == ReleaseStatus.Published)
            release.PublishedAt = nowUtc;
        return true;
    }
}
