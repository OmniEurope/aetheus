// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Configuration;

namespace Aetheus.Back.Components.Git;

/// <summary>
/// Content-mutating git operations (commit files, set HEAD, detect default branch). Extracted from
/// the former <c>GitLightCliService.Write.cs</c> partial into a real collaborator; shares the
/// underlying git executor via <see cref="GitProcessRunner"/>. <see cref="GitLightCliService"/>
/// delegates its write methods here so the public <see cref="IGitLightCliService"/> contract is
/// unchanged.
/// </summary>
public class GitLightCliWriter(GitProcessRunner git, ILogger<GitLightCliWriter> logger)
{
    public async Task<(bool Success, string? CommitSha, string? Error)> CommitFileAsync(
        string diskPath, string branch, string relativePath, string content,
        string commitMessage, string authorName, string authorEmail, CancellationToken ct = default)
        => await CommitFilesAsync(diskPath, branch, [(relativePath, content)], commitMessage, authorName, authorEmail, ct).ConfigureAwait(false);

    /// <summary>
    /// Commits N files in a single clone + commit + push round-trip (F-007: the env→project
    /// pipeline copy used to clone once per file inside the HTTP request path).
    /// </summary>
    public async Task<(bool Success, string? CommitSha, string? Error)> CommitFilesAsync(
        string diskPath, string branch, IReadOnlyList<(string RelativePath, string Content)> files,
        string commitMessage, string authorName, string authorEmail, CancellationToken ct = default)
        => await CommitFileChangesAsync(
            diskPath, branch, files, [], commitMessage, authorName, authorEmail, ct).ConfigureAwait(false);

    public async Task<(bool Success, string? CommitSha, string? Error)> CommitFileChangesAsync(
        string diskPath, string branch,
        IReadOnlyList<(string RelativePath, string Content)> upserts,
        IReadOnlyList<string> deletions,
        string commitMessage, string authorName, string authorEmail, CancellationToken ct = default)
    {
        if (upserts.Count == 0 && deletions.Count == 0) return (true, null, null);

        // F-038: the branch name reaches git argv (checkout / push refspec) - reject anything
        // that is not a plausible refname before any process is spawned.
        if (!IsValidRefName(branch))
            return (false, null, "invalid branch name");

        // F-037: validate paths BEFORE the clone (no wasted work on rejection). Segment-wise
        // ".." check - "a..b.yaml" is a legitimate file name, a "..\" segment is traversal.
        var safeUpserts = new List<string>(upserts.Count);
        foreach (var (relativePath, _) in upserts)
        {
            if (!TryNormalizeRelativePath(relativePath, out var safeRel))
                return (false, null, $"invalid relative path: {relativePath}");
            safeUpserts.Add(safeRel);
        }
        var safeDeletions = new List<string>(deletions.Count);
        foreach (var relativePath in deletions)
        {
            if (!TryNormalizeRelativePath(relativePath, out var safeRel))
                return (false, null, $"invalid relative path: {relativePath}");
            if (!safeUpserts.Contains(safeRel, StringComparer.Ordinal))
                safeDeletions.Add(safeRel);
        }

        // Temp clone of the (local, bare) repo - no auth, fast. Works for empty repos too:
        // the first push creates the branch. A worktree would choke on an empty repo (no HEAD).
        var tmp = Path.Combine(Path.GetTempPath(), $"aetheus-commit-{Guid.NewGuid():N}");
        try
        {
            // F-018: shallow single-branch clone - the repo can be large, we only commit on top.
            // Falls back to a full clone when the branch doesn't exist yet (empty/new repo).
            var (exitClone, _, _) = await git.RunGitAsync(diskPath,
                ["clone", "--depth", "1", "--single-branch", "--no-tags", "--branch", branch, diskPath, tmp],
                ct, timeout: BackendRuntimeDefaults.GitWriteTimeout, ignoreExitCode: true).ConfigureAwait(false);
            if (exitClone != 0)
            {
                var (exitFull, _, errFull) = await git.RunGitAsync(diskPath, ["clone", diskPath, tmp], ct, timeout: BackendRuntimeDefaults.GitWriteTimeout).ConfigureAwait(false);
                if (exitFull != 0) return (false, null, $"clone failed: {errFull}");

                // Position on the target branch (existing branch → checkout; empty/new → create).
                var (exitCo, _, _) = await git.RunGitAsync(tmp, ["checkout", branch], ct, ignoreExitCode: true).ConfigureAwait(false);
                if (exitCo != 0)
                    await git.RunGitAsync(tmp, ["checkout", "-b", branch], ct, ignoreExitCode: true).ConfigureAwait(false);
            }

            await git.RunGitAsync(tmp, ["config", "user.name", authorName], ct).ConfigureAwait(false);
            await git.RunGitAsync(tmp, ["config", "user.email", authorEmail], ct).ConfigureAwait(false);

            for (var i = 0; i < upserts.Count; i++)
            {
                // Belt-and-braces canonical check now that the clone root exists.
                var target = Path.GetFullPath(Path.Combine(tmp, safeUpserts[i]));
                if (!target.StartsWith(Path.GetFullPath(tmp) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    return (false, null, $"invalid relative path: {upserts[i].RelativePath}");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await File.WriteAllTextAsync(target, upserts[i].Content, ct).ConfigureAwait(false);
                await git.RunGitAsync(tmp, ["add", "--", safeUpserts[i]], ct).ConfigureAwait(false);
            }
            foreach (var safeRel in safeDeletions.Distinct(StringComparer.Ordinal))
            {
                await git.RunGitAsync(
                    tmp, ["rm", "--ignore-unmatch", "--", safeRel], ct, ignoreExitCode: true)
                    .ConfigureAwait(false);
            }

            var (exitCommit, _, errCommit) = await git.RunGitAsync(tmp, ["commit", "-m", commitMessage], ct, ignoreExitCode: true).ConfigureAwait(false);
            if (exitCommit != 0)
            {
                // Non-zero commit is EITHER "nothing to commit" (identical content → no-op success)
                // OR a real failure (e.g. a commit hook rejected it). Distinguish via the work tree:
                // a clean tree means the content was already there; a dirty tree means the commit failed.
                var (_, statusOut, _) = await git.RunGitAsync(tmp, ["status", "--porcelain"], ct, ignoreExitCode: true).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(statusOut))
                    return (false, null, $"commit failed: {errCommit}");

                var (_, headSha, _) = await git.RunGitAsync(tmp, ["rev-parse", "HEAD"], ct, ignoreExitCode: true).ConfigureAwait(false);
                return (true, headSha.Trim(), null); // no-op: nothing changed, nothing to push
            }

            var (_, sha, _) = await git.RunGitAsync(tmp, ["rev-parse", "HEAD"], ct, ignoreExitCode: true).ConfigureAwait(false);
            var (exitPush, _, errPush) = await git.RunGitAsync(tmp, ["push", "origin", $"HEAD:{branch}"], ct, timeout: BackendRuntimeDefaults.GitWriteTimeout).ConfigureAwait(false);
            if (exitPush != 0) return (false, null, $"push failed: {errPush}");

            return (true, sha.Trim(), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (false, null, ex.Message);
        }
        finally
        {
            if (Directory.Exists(tmp))
                try { GitProcessRunner.ClearReadOnlyAttributes(tmp); Directory.Delete(tmp, true); }
                catch (Exception ex) { logger.LogDebug(ex, "Best-effort commit temp cleanup failed: {Dir}", tmp); }
        }
    }

    private static bool TryNormalizeRelativePath(string relativePath, out string safeRelativePath)
    {
        safeRelativePath = relativePath.Replace('\\', '/');
        return !string.IsNullOrWhiteSpace(safeRelativePath)
            && !Path.IsPathRooted(safeRelativePath)
            && safeRelativePath.Split('/').All(seg => seg is not (".." or ""));
    }

    public async Task<string?> DetectDefaultBranchAsync(string diskPath, CancellationToken ct = default)
    {
        if (!Directory.Exists(diskPath)) return null;

        var (lsCode, lsOut, _) = await git.RunGitAsync(diskPath,
            ["for-each-ref", "--format=%(refname:short)", "refs/heads/"], ct, ignoreExitCode: true).ConfigureAwait(false);
        if (lsCode != 0 || string.IsNullOrWhiteSpace(lsOut)) return null;

        var branches = lsOut
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (branches.Count == 0) return null;

        var (headCode, headOut, _) = await git.RunGitAsync(diskPath,
            ["symbolic-ref", "--quiet", "--short", "HEAD"], ct, ignoreExitCode: true).ConfigureAwait(false);
        if (headCode == 0)
        {
            var headBranch = headOut.Trim();
            if (!string.IsNullOrEmpty(headBranch) &&
                branches.Contains(headBranch, StringComparer.Ordinal))
                return headBranch;
        }

        foreach (var preferred in new[] { "main", "master", "develop", "trunk" })
        {
            if (branches.Contains(preferred, StringComparer.Ordinal))
                return preferred;
        }
        return branches[0];
    }

    public async Task SetHeadAsync(string diskPath, string branch, CancellationToken ct = default)
    {
        // Defense-in-depth, consistent with CommitFilesAsync: validate the ref name before it is
        // interpolated into refs/heads/<branch> and passed to git symbolic-ref. Callers feed this from
        // branch detection today, but the public contract must not assume that.
        if (!IsValidRefName(branch))
            throw new ArgumentException($"Invalid branch name '{branch}'.", nameof(branch));
        await git.RunGitAsync(diskPath, ["symbolic-ref", "HEAD", $"refs/heads/{branch}"], ct).ConfigureAwait(false);
    }

    /// <summary>F-038: conservative refname validation (subset of git-check-ref-format).</summary>
    private static bool IsValidRefName(string branch)
    {
        if (string.IsNullOrWhiteSpace(branch) || branch.Length > 255) return false;
        if (branch.StartsWith('-') || branch.StartsWith('/') || branch.EndsWith('/')) return false;
        if (branch.EndsWith(".lock", StringComparison.Ordinal) || branch.EndsWith('.')) return false;
        if (branch.Contains("..", StringComparison.Ordinal) || branch.Contains("//", StringComparison.Ordinal)) return false;
        return branch.All(c => !char.IsControl(c) && c is not (' ' or '~' or '^' or ':' or '?' or '*' or '[' or '\\'));
    }
}
