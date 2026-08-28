// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Git;

namespace Aetheus.Back.Data;

internal sealed class TotoConformanceRepositorySeeder(
    IGitLightService git,
    IGitLightCliService gitCli)
{
    private const string SeedActor = "toto-conformance-seed";

    internal async Task SeedAsync(
        int projectId,
        GitLightRepoDto repository,
        IReadOnlyDictionary<string, string> pipelineFiles,
        CancellationToken ct)
    {
        var (diskPath, tags, branches) =
            await ReadRepositoryStateAsync(projectId, repository, ct).ConfigureAwait(false);
        var hasBaselineTag = tags.Any(tag =>
            string.Equals(tag.Name, TotoConformanceSeeder.BaselineTag, StringComparison.Ordinal));

        if (!hasBaselineTag)
        {
            if (branches.Count > 0)
                throw new InvalidOperationException(
                    "Toto conformance repository contains branches but no V-1 tag; refusing to overwrite a partial seed.");

            var baselineFiles = TotoConformanceSeeder.MergeRepositoryFiles("v1", pipelineFiles);
            var baselineCommit = await CommitFilesAsync(
                diskPath, repository.DefaultBranch, baselineFiles,
                "chore(conformance): seed deterministic Toto V-1", ct).ConfigureAwait(false);
            await git.CreateTagAsync(repository.Id, new CreateGitLightTagRequest
            {
                Name = TotoConformanceSeeder.BaselineTag,
                Ref = baselineCommit,
                Message = "Deterministic Toto rollback baseline"
            }, ct).ConfigureAwait(false);
            tags = await git.GetTagsAsync(repository.Id, ct).ConfigureAwait(false);
            branches = await git.GetBranchesAsync(repository.Id, ct).ConfigureAwait(false);
        }

        if (!branches.Any(branch =>
                string.Equals(branch.Name, TotoConformanceSeeder.BaselineBranch, StringComparison.Ordinal)))
        {
            await git.CreateBranchAsync(repository.Id, new CreateGitLightBranchRequest
            {
                Name = TotoConformanceSeeder.BaselineBranch,
                StartRef = TotoConformanceSeeder.BaselineTag
            }, ct).ConfigureAwait(false);
        }

        await CommitFilesAsync(
            diskPath,
            TotoConformanceSeeder.BaselineBranch,
            TotoConformanceSeeder.MergeRepositoryFiles("v1", pipelineFiles),
            "chore(conformance): reconcile V-1 pipeline contracts",
            ct).ConfigureAwait(false);

        var currentCommit = await CommitFilesAsync(
            diskPath,
            repository.DefaultBranch,
            TotoConformanceSeeder.MergeRepositoryFiles("v2", pipelineFiles),
            "feat(conformance): seed forward-compatible Toto V2",
            ct).ConfigureAwait(false);

        if (!tags.Any(tag =>
                string.Equals(tag.Name, TotoConformanceSeeder.CurrentTag, StringComparison.Ordinal)))
        {
            await git.CreateTagAsync(repository.Id, new CreateGitLightTagRequest
            {
                Name = TotoConformanceSeeder.CurrentTag,
                Ref = currentCommit,
                Message = "Deterministic Toto current candidate"
            }, ct).ConfigureAwait(false);
        }

        await EnsureDevelopBranchAsync(repository, ct).ConfigureAwait(false);
    }

    internal async Task SeedVulnerableAsync(
        int projectId,
        GitLightRepoDto repository,
        IReadOnlyDictionary<string, string> pipelineFiles,
        CancellationToken ct)
    {
        var state = await ReadRepositoryStateAsync(projectId, repository, ct).ConfigureAwait(false);
        var diskPath = state.DiskPath;
        var tags = state.Tags;
        var branches = state.Branches;
        var hasVulnerableTag = tags.Any(tag =>
            string.Equals(tag.Name, TotoConformanceSeeder.VulnerableTag, StringComparison.Ordinal));

        if (!hasVulnerableTag && branches.Count > 0)
            throw new InvalidOperationException(
                "Toto Vulnerable repository contains branches but no fixture tag; refusing to overwrite a partial seed.");

        var currentCommit = await CommitFilesAsync(
            diskPath,
            repository.DefaultBranch,
            TotoConformanceSeeder.MergeVulnerableRepositoryFiles(pipelineFiles),
            "test(conformance): seed deterministic Toto Vulnerable",
            ct).ConfigureAwait(false);

        if (!hasVulnerableTag)
        {
            await git.CreateTagAsync(repository.Id, new CreateGitLightTagRequest
            {
                Name = TotoConformanceSeeder.VulnerableTag,
                Ref = currentCommit,
                Message = "Deterministically vulnerable Toto source"
            }, ct).ConfigureAwait(false);
        }

        await EnsureDevelopBranchAsync(repository, ct).ConfigureAwait(false);
    }

    private async Task<RepositoryState> ReadRepositoryStateAsync(
        int projectId,
        GitLightRepoDto repository,
        CancellationToken ct)
    {
        await git.EnsureRepositoryInitializedAsync(repository.Id, ct).ConfigureAwait(false);
        return new RepositoryState(
            git.ResolveDiskPath(projectId, repository.Slug),
            await git.GetTagsAsync(repository.Id, ct).ConfigureAwait(false),
            await git.GetBranchesAsync(repository.Id, ct).ConfigureAwait(false));
    }

    private async Task EnsureDevelopBranchAsync(GitLightRepoDto repository, CancellationToken ct)
    {
        var branches = await git.GetBranchesAsync(repository.Id, ct).ConfigureAwait(false);
        if (branches.Any(branch =>
                string.Equals(branch.Name, TotoConformanceSeeder.DevelopBranch, StringComparison.Ordinal)))
            return;

        await git.CreateBranchAsync(repository.Id, new CreateGitLightBranchRequest
        {
            Name = TotoConformanceSeeder.DevelopBranch,
            StartRef = repository.DefaultBranch
        }, ct).ConfigureAwait(false);
    }

    private async Task<string> CommitFilesAsync(
        string diskPath,
        string branch,
        IReadOnlyDictionary<string, string> files,
        string message,
        CancellationToken ct)
    {
        var changes = files.OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => (entry.Key, entry.Value))
            .ToArray();
        var (success, commitSha, error) = await gitCli.CommitFilesAsync(
            diskPath, branch, changes, message, SeedActor, "conformance@aetheus.local", ct).ConfigureAwait(false);
        if (!success || string.IsNullOrWhiteSpace(commitSha))
            throw new InvalidOperationException($"Could not seed Toto branch '{branch}': {error ?? "missing commit"}.");
        return commitSha;
    }

    private sealed record RepositoryState(
        string DiskPath,
        List<GitLightTagDto> Tags,
        List<GitLightBranchDto> Branches);
}
