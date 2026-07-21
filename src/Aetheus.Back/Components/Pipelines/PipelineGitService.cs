// SPDX-License-Identifier: EUPL-1.2
using System.Text;
using Aetheus.Back.Components.Git;
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Components.Pipelines;

// Git-strict storage for project-owned pipelines (internal repos only). See IPipelineGitService.
// F-030: depends on the Git module's service contracts only (no cross-slice repository reach-in).
public sealed class PipelineGitService(
    IGitLightService gitService,
    IGitLightCliService cli,
    IPipelineRepository pipelineRepo,
    ILogger<PipelineGitService> logger) : IPipelineGitService
{
    private const string PipelineDir = ".pipeline";

    public async Task<string?> ReadProjectPipelineYamlAsync(int projectId, string pipelineName, CancellationToken ct = default, string? sourceBranch = null)
        => await ReadProjectPipelineYamlCoreAsync(projectId, pipelineName, sourceBranch, ct).ConfigureAwait(false);

    public async Task<string?> ReadProjectPipelineYamlAtRevisionAsync(
        int projectId, string pipelineName, string revision, CancellationToken ct = default)
        => await ReadProjectPipelineYamlCoreAsync(projectId, pipelineName, revision, ct).ConfigureAwait(false);

    private async Task<string?> ReadProjectPipelineYamlCoreAsync(
        int projectId, string pipelineName, string? revision, CancellationToken ct)
    {
        var repo = await GetProjectRepoAsync(projectId, ct).ConfigureAwait(false);
        if (repo is null || repo.IsEmpty) return null;

        var diskPath = SafeDiskPath(projectId, repo);
        // Guard the run's critical path: a DB repo row with no bare repo on disk must fall back to
        // the DB definition, never throw (git would Win32Exception on a missing working directory).
        if (diskPath is null || !Directory.Exists(diskPath)) return null;

        var branch = string.IsNullOrWhiteSpace(revision)
            ? string.IsNullOrWhiteSpace(repo.DefaultBranch) ? "HEAD" : repo.DefaultBranch
            : revision;

        // F-019: fast path - the UI writes `.pipeline/{slug}.yaml`, so a single `git show` resolves
        // the common case without enumerating (and parsing) every YAML in the directory.
        var directBlob = await cli.GetBlobAsync(diskPath, branch, $"{PipelineDir}/{Slugify(pipelineName)}.yaml", ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(directBlob?.Content))
        {
            var directDef = YamlParsingHelper.ParseAndValidate(directBlob.Content, logger);
            var directName = string.IsNullOrWhiteSpace(directDef?.Name) ? Slugify(pipelineName) : directDef!.Name;
            if (string.Equals(directName, pipelineName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(directName, Slugify(pipelineName), StringComparison.OrdinalIgnoreCase))
                return directBlob.Content;
        }

        var tree = await cli.GetTreeAsync(diskPath, branch, PipelineDir, ct).ConfigureAwait(false);
        foreach (var file in tree.Where(IsYaml))
        {
            var blob = await cli.GetBlobAsync(diskPath, branch, $"{PipelineDir}/{file.Name}", ct).ConfigureAwait(false);
            if (blob?.Content is null || string.IsNullOrWhiteSpace(blob.Content)) continue;

            var def = YamlParsingHelper.ParseAndValidate(blob.Content, logger);
            var nameInFile = def?.Name;
            if (string.IsNullOrWhiteSpace(nameInFile))
                nameInFile = Path.GetFileNameWithoutExtension(file.Name);

            if (string.Equals(nameInFile, pipelineName, StringComparison.OrdinalIgnoreCase))
                return blob.Content;
        }
        return null;
    }

    public async Task<string?> GetHeadCommitShaAsync(int projectId, CancellationToken ct = default, string? sourceBranch = null)
    {
        try
        {
            var repo = await GetProjectRepoAsync(projectId, ct).ConfigureAwait(false);
            if (repo is null || repo.IsEmpty) return null;

            var diskPath = SafeDiskPath(projectId, repo);
            if (diskPath is null || !Directory.Exists(diskPath)) return null;

            var branch = string.IsNullOrWhiteSpace(sourceBranch)
                ? string.IsNullOrWhiteSpace(repo.DefaultBranch) ? "HEAD" : repo.DefaultBranch
                : sourceBranch;
            var commits = await cli.GetCommitsAsync(diskPath, branch, skip: 0, take: 1, ct: ct).ConfigureAwait(false);
            return commits.FirstOrDefault()?.Sha;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Never let git context resolution break the run trigger - commit is informational.
            logger.LogWarning(ex, "Could not resolve head commit for project {ProjectId}", projectId);
            return null;
        }
    }

    public async Task<PipelineSourceDto?> GetPipelineSourceAsync(
        int projectId, string pipelineName, CancellationToken ct = default, string? sourceBranch = null)
    {
        var repo = await GetProjectRepoAsync(projectId, ct).ConfigureAwait(false);
        if (repo is null) return null;

        var branch = string.IsNullOrWhiteSpace(sourceBranch)
            ? string.IsNullOrWhiteSpace(repo.DefaultBranch) ? "main" : repo.DefaultBranch
            : sourceBranch;
        var commit = await GetHeadCommitShaAsync(projectId, ct, sourceBranch).ConfigureAwait(false);
        return new PipelineSourceDto
        {
            RepositoryId = repo.Id,
            Path = $"{PipelineDir}/{Slugify(pipelineName)}.yaml",
            Branch = branch,
            CommitHash = commit
        };
    }

    public async Task<(GitWriteOutcome Outcome, string? Error)> WriteProjectPipelineYamlAsync(
        int projectId, string pipelineName, string yaml, string actor, CancellationToken ct = default, string? sourceBranch = null)
    {
        var repo = await GetProjectRepoAsync(projectId, ct).ConfigureAwait(false);
        // No internal repo: the project is external/DB-only - a non-error fallback, not a git failure.
        if (repo is null) return (GitWriteOutcome.NoRepo, null);

        // An internal repo row with no bare repo on disk is a real, surfaceable failure: git is the
        // source of truth, so we must not silently fall back to a DB-only definition.
        var diskPath = SafeDiskPath(projectId, repo);
        if (diskPath is null || !Directory.Exists(diskPath)) return (GitWriteOutcome.Failed, "repository not initialized on disk");

        var branch = string.IsNullOrWhiteSpace(sourceBranch)
            ? string.IsNullOrWhiteSpace(repo.DefaultBranch) ? "main" : repo.DefaultBranch
            : sourceBranch;
        var relPath = $"{PipelineDir}/{Slugify(pipelineName)}.yaml";
        var (ok, _, error) = await cli.CommitFileAsync(
            diskPath, branch, relPath, yaml,
            $"chore(pipeline): update {pipelineName} via Aetheus",
            actor, $"{actor}@aetheus", ct).ConfigureAwait(false);
        if (!ok) logger.LogWarning("Failed to write pipeline '{Name}' to {Repo}: {Error}", pipelineName, repo.Slug, error);
        return (ok ? GitWriteOutcome.Committed : GitWriteOutcome.Failed, error);
    }

    public async Task<(GitWriteOutcome Outcome, string? Error)> ApplyProjectPipelineChangeAsync(
        PipelineGitDefinitionLocation? previous,
        PipelineGitDefinitionLocation? next,
        string? nextYaml,
        string actor,
        CancellationToken ct = default)
    {
        if (next is not null && nextYaml is null)
            throw new ArgumentNullException(nameof(nextYaml));

        var (oldTarget, oldError) = await ResolveWriteTargetAsync(previous, ct).ConfigureAwait(false);
        if (oldError is not null) return (GitWriteOutcome.Failed, oldError);
        var (newTarget, newError) = await ResolveWriteTargetAsync(next, ct).ConfigureAwait(false);
        if (newError is not null) return (GitWriteOutcome.Failed, newError);
        if (oldTarget is null && newTarget is null) return (GitWriteOutcome.NoRepo, null);

        if (oldTarget is not null && newTarget is not null
            && string.Equals(oldTarget.DiskPath, newTarget.DiskPath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(oldTarget.Branch, newTarget.Branch, StringComparison.Ordinal))
        {
            var deletions = string.Equals(oldTarget.RelativePath, newTarget.RelativePath, StringComparison.Ordinal)
                ? Array.Empty<string>()
                : [oldTarget.RelativePath];
            var (ok, _, error) = await cli.CommitFileChangesAsync(
                newTarget.DiskPath, newTarget.Branch,
                [(newTarget.RelativePath, nextYaml!)], deletions,
                $"chore(pipeline): update {next!.PipelineName} via Aetheus",
                actor, $"{actor}@aetheus", ct).ConfigureAwait(false);
            return (ok ? GitWriteOutcome.Committed : GitWriteOutcome.Failed, error);
        }

        if (newTarget is not null)
        {
            var (written, _, writeError) = await cli.CommitFileChangesAsync(
                newTarget.DiskPath, newTarget.Branch,
                [(newTarget.RelativePath, nextYaml!)], [],
                $"chore(pipeline): add {next!.PipelineName} via Aetheus",
                actor, $"{actor}@aetheus", ct).ConfigureAwait(false);
            if (!written) return (GitWriteOutcome.Failed, writeError);
        }

        if (oldTarget is not null)
        {
            var (deleted, _, deleteError) = await cli.CommitFileChangesAsync(
                oldTarget.DiskPath, oldTarget.Branch, [], [oldTarget.RelativePath],
                $"chore(pipeline): remove {previous!.PipelineName} via Aetheus",
                actor, $"{actor}@aetheus", ct).ConfigureAwait(false);
            if (!deleted)
            {
                if (newTarget is not null)
                {
                    var (rolledBack, _, rollbackError) = await cli.CommitFileChangesAsync(
                        newTarget.DiskPath, newTarget.Branch, [], [newTarget.RelativePath],
                        $"revert(pipeline): roll back {next!.PipelineName} move",
                        actor, $"{actor}@aetheus", ct).ConfigureAwait(false);
                    if (!rolledBack)
                        return (GitWriteOutcome.Failed,
                            $"{deleteError}; destination rollback also failed: {rollbackError}");
                }
                return (GitWriteOutcome.Failed, deleteError);
            }
        }

        return (GitWriteOutcome.Committed, null);
    }

    private async Task<(PipelineWriteTarget? Target, string? Error)> ResolveWriteTargetAsync(
        PipelineGitDefinitionLocation? location, CancellationToken ct)
    {
        if (location is null) return (null, null);
        var repo = await GetProjectRepoAsync(location.ProjectId, ct).ConfigureAwait(false);
        if (repo is null) return (null, null);
        var diskPath = SafeDiskPath(location.ProjectId, repo);
        if (diskPath is null || !Directory.Exists(diskPath))
            return (null, "repository not initialized on disk");
        var branch = string.IsNullOrWhiteSpace(location.SourceBranch)
            ? string.IsNullOrWhiteSpace(repo.DefaultBranch) ? "main" : repo.DefaultBranch
            : location.SourceBranch;
        return (new PipelineWriteTarget(
            diskPath, branch, $"{PipelineDir}/{Slugify(location.PipelineName)}.yaml"), null);
    }

    private sealed record PipelineWriteTarget(string DiskPath, string Branch, string RelativePath);

    public async Task<int> CopyEnvironmentPipelinesToProjectAsync(
        int environmentId, string environmentName, int projectId, string actor, CancellationToken ct = default)
    {
        var repo = await GetProjectRepoAsync(projectId, ct).ConfigureAwait(false);
        if (repo is null) return 0; // external-repo projects: nothing to copy into

        var diskPath = SafeDiskPath(projectId, repo);
        if (diskPath is null || !Directory.Exists(diskPath)) return 0;

        var branch = string.IsNullOrWhiteSpace(repo.DefaultBranch) ? "main" : repo.DefaultBranch;
        var pipelines = await pipelineRepo.GetPipelinesByEnvironmentAsync(environmentId, ct).ConfigureAwait(false);

        // F-007: one clone + one commit + one push for the whole batch - the copy runs inside the
        // UpdateEnvironment request path and must not scale its latency with the pipeline count.
        var files = pipelines
            .Where(p => !string.IsNullOrWhiteSpace(p.YamlDefinition))
            .Select(p => (RelativePath: $"{PipelineDir}/{Slugify(environmentName)}-{Slugify(p.Name)}.yaml", Content: p.YamlDefinition))
            .ToList();
        if (files.Count == 0) return 0;

        var (ok, _, error) = await cli.CommitFilesAsync(
            diskPath, branch, files,
            $"chore(pipeline): copy {files.Count} pipeline(s) from environment '{environmentName}' via Aetheus",
            actor, $"{actor}@aetheus", ct).ConfigureAwait(false);
        if (!ok)
        {
            logger.LogWarning("Failed to copy env '{Env}' pipelines to project {ProjectId}: {Error}", environmentName, projectId, error);
            return 0;
        }
        return files.Count;
    }

    // The project's primary internal repo. A project may host several; the first by name owns
    // `.pipeline/`. Null when the project is external-repo-only.
    private async Task<GitLightRepoDto?> GetProjectRepoAsync(int projectId, CancellationToken ct)
    {
        var repos = await gitService.GetRepositoriesAsync(projectId, ct).ConfigureAwait(false);
        return repos.FirstOrDefault();
    }

    private string? SafeDiskPath(int projectId, GitLightRepoDto repo)
    {
        try { return gitService.ResolveDiskPath(projectId, repo.Slug); }
        catch (Exception ex) { logger.LogWarning(ex, "Invalid repo path for {Slug}", repo.Slug); return null; }
    }

    private static bool IsYaml(GitLightTreeEntryDto e)
        => e.Type == GitTreeEntryType.Blob
           && (e.Name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
               || e.Name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase));

    private static string Slugify(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name.Trim().ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '-');
        var slug = sb.ToString().Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        return string.IsNullOrEmpty(slug) ? "pipeline" : slug;
    }
}
