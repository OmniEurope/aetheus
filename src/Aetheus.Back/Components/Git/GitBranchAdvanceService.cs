// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Configuration;
using Microsoft.Extensions.Options;

namespace Aetheus.Back.Components.Git;

/// <summary>Why a branch advance did or did not happen. Every outcome is reported, never swallowed:
/// a silent no-op here is exactly the failure mode this replaced.</summary>
public enum GitBranchAdvanceOutcome
{
    /// <summary>The branch now points at the requested commit (it was created or fast-forwarded).</summary>
    Advanced,

    /// <summary>The branch already pointed at that commit. Nothing to do.</summary>
    AlreadyUpToDate,

    /// <summary>The commit is not a descendant of the branch tip - the classic rollback case. Refused
    /// on purpose: the branch keeps trailing what is actually live and realigns on the next forward
    /// deployment.</summary>
    NotFastForward,

    /// <summary>No internal repository of the project contains that commit (an external-only project,
    /// or a commit that never reached this instance).</summary>
    CommitNotFound,

    /// <summary>The request itself was malformed (bad branch name or commit id), or git refused the
    /// ref update. Details are in <see cref="GitBranchAdvanceResult.Detail"/>.</summary>
    Refused
}

/// <param name="RepositorySlug">The internal repository the advance applied to, when one matched.</param>
/// <param name="PreviousSha">The branch tip before the update; null when the branch did not exist.</param>
public sealed record GitBranchAdvanceResult(
    GitBranchAdvanceOutcome Outcome,
    string? RepositorySlug = null,
    string? PreviousSha = null,
    string? Detail = null);

public interface IGitBranchAdvanceService
{
    /// <summary>Fast-forwards <paramref name="branch"/> onto <paramref name="targetSha"/> in whichever
    /// internal repository of <paramref name="projectId"/> already contains that commit. Never rewrites
    /// history and never throws; the caller reads the outcome.</summary>
    Task<GitBranchAdvanceResult> TryFastForwardAsync(
        int projectId, string branch, string targetSha, CancellationToken ct = default);
}

/// <summary>
/// Advances a branch inside an Aetheus-hosted repository directly on disk, with git's own
/// <c>update-ref</c> compare-and-swap as the concurrency guard.
///
/// This deliberately does NOT go through <c>git push</c>: Aetheus hosts the repository it would be
/// pushing to, so a push would mean the instance authenticating to itself with a credential someone
/// has to provision, rotate, and store on the deployment host. Writing the ref in place removes the
/// credential entirely. It also means the update does not run the receive-pack path, so no
/// <c>GitPushProcessedEvent</c> is raised - moving the branch onto an already-built, already-deployed
/// commit must not re-trigger the pipelines that watch it.
/// </summary>
public sealed class GitBranchAdvanceService(
    IGitLightRepository lightRepo,
    GitProcessRunner git,
    IOptions<GitLightOptions> options,
    ILogger<GitBranchAdvanceService> logger) : IGitBranchAdvanceService
{
    private readonly GitLightOptions _options = options.Value;

    public async Task<GitBranchAdvanceResult> TryFastForwardAsync(
        int projectId, string branch, string targetSha, CancellationToken ct = default)
    {
        if (!GitBranchNameValidator.IsValid(branch))
            return new GitBranchAdvanceResult(GitBranchAdvanceOutcome.Refused, Detail: $"invalid branch name '{branch}'");
        if (!IsCommitId(targetSha))
            return new GitBranchAdvanceResult(GitBranchAdvanceOutcome.Refused, Detail: "target is not a full commit id");

        var repos = await lightRepo.GetByProjectAsync(projectId, ct).ConfigureAwait(false);
        foreach (var repo in repos)
        {
            var diskPath = GitRepoPathResolver.TryResolve(_options.RepositoriesPath, repo.ProjectId, repo.Slug);
            if (diskPath is null) continue;

            // Only the repository that actually holds the commit is a candidate. A project with
            // several repositories therefore advances the right one instead of the first one.
            var (containsCode, _, _) = await git.RunGitAsync(
                diskPath, ["cat-file", "-e", $"{targetSha}^{{commit}}"], ct, ignoreExitCode: true).ConfigureAwait(false);
            if (containsCode != 0) continue;

            return await AdvanceInRepositoryAsync(diskPath, repo.Slug, branch, targetSha, ct).ConfigureAwait(false);
        }

        return new GitBranchAdvanceResult(GitBranchAdvanceOutcome.CommitNotFound);
    }

    private async Task<GitBranchAdvanceResult> AdvanceInRepositoryAsync(
        string diskPath, string slug, string branch, string targetSha, CancellationToken ct)
    {
        var reference = $"refs/heads/{branch}";
        var (revCode, revOut, _) = await git.RunGitAsync(
            diskPath, ["rev-parse", "--verify", "--quiet", reference], ct, ignoreExitCode: true).ConfigureAwait(false);
        // --quiet turns "no such ref" into exit 1 with empty output, which is how a branch that does
        // not exist yet is told apart from a genuine failure.
        var previousSha = revCode == 0 ? revOut.Trim() : null;

        if (string.Equals(previousSha, targetSha, StringComparison.OrdinalIgnoreCase))
            return new GitBranchAdvanceResult(GitBranchAdvanceOutcome.AlreadyUpToDate, slug, previousSha);

        if (previousSha is not null)
        {
            var (ancestorCode, _, _) = await git.RunGitAsync(
                diskPath, ["merge-base", "--is-ancestor", previousSha, targetSha], ct, ignoreExitCode: true)
                .ConfigureAwait(false);
            if (ancestorCode != 0)
            {
                logger.LogInformation(
                    "Branch {Branch} of {Slug} left at {Previous}: {Target} is not a descendant (rollback or diverged history).",
                    branch, slug, previousSha, targetSha);
                return new GitBranchAdvanceResult(GitBranchAdvanceOutcome.NotFastForward, slug, previousSha);
            }
        }

        // The trailing old-value argument makes this a compare-and-swap: git refuses the update if the
        // ref moved since it was read, so a concurrent push cannot be silently overwritten. An empty
        // old value asserts "this ref must not exist", which is the branch-creation case.
        var (updateCode, _, updateError) = await git.RunGitAsync(
            diskPath, ["update-ref", reference, targetSha, previousSha ?? string.Empty], ct, ignoreExitCode: true)
            .ConfigureAwait(false);
        if (updateCode != 0)
        {
            logger.LogWarning(
                "Branch {Branch} of {Slug} was not advanced to {Target}: git update-ref failed ({Error}).",
                branch, slug, targetSha, updateError.Trim());
            return new GitBranchAdvanceResult(
                GitBranchAdvanceOutcome.Refused, slug, previousSha, updateError.Trim());
        }

        logger.LogInformation(
            "Branch {Branch} of {Slug} advanced from {Previous} to {Target}.",
            branch, slug, previousSha ?? "(new)", targetSha);
        return new GitBranchAdvanceResult(GitBranchAdvanceOutcome.Advanced, slug, previousSha);
    }

    // SHA-1 (40) and SHA-256 (64) object ids, the two hash lengths git repositories use.
    private static bool IsCommitId(string? value) =>
        value is { Length: 40 or 64 } && value.All(Uri.IsHexDigit);
}
