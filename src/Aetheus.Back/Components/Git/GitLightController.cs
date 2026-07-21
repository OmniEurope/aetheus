// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Back.Services;
using Aetheus.Shared.Constants;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aetheus.Back.Components.Git;

[ApiController]
[Route("api/git/repos")]
[Authorize]
public class GitLightController(IGitLightService service, IResourceAuthorizationService authz) : ControllerBase
{
    private async Task<(bool Allowed, ActionResult? Failure)> CheckRepoAccessAsync(int repoId, Permission perm, CancellationToken ct)
    {
        var projectId = await service.GetProjectIdForRepoAsync(repoId, ct);
        if (projectId is null) return (false, NotFound());
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, projectId.Value, perm, ct))
            return (false, Forbid());
        return (true, null);
    }

    // ── Repositories ─────────────────────────────────────────────────

    [HttpGet]
    public async Task<ActionResult<PaginatedResult<GitLightRepoDto>>> GetRepositories(
        [FromQuery] int? projectId, [FromQuery] PaginationRequest request, CancellationToken ct)
    {
        // Scoped to one project (the dropdown filter): require Read on that project.
        if (projectId is not null)
        {
            if (!await authz.HasPermissionAsync(User, ResourceType.Project, projectId.Value, Permission.Read, ct))
                return Forbid();
            return Ok(await service.GetRepositoriesPageAsync(
                projectId, [projectId.Value], request, ct));
        }

        // No filter: list every repo across the projects the caller can read. GetAccessibleResourceIdsAsync
        // returns null for an unrestricted admin (=> all repos) and an empty list for a user with no
        // project access (=> no repos).
        var accessibleIds = await authz.GetAccessibleResourceIdsAsync(User, ResourceType.Project, Permission.Read, ct);
        if (accessibleIds is { Count: 0 })
        {
            var (page, pageSize) = request.Normalize();
            return Ok(new PaginatedResult<GitLightRepoDto> { Page = page, PageSize = pageSize });
        }
        return Ok(await service.GetRepositoriesPageAsync(null, accessibleIds, request, ct));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<GitLightRepoDto>> GetRepository(int id, CancellationToken ct)
    {
        var (ok, fail) = await CheckRepoAccessAsync(id, Permission.Read, ct);
        if (!ok) return fail!;

        var result = await service.GetRepositoryAsync(id, ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<GitLightRepoDto>> CreateRepository([FromBody] CreateGitLightRepoRequest request, CancellationToken ct)
    {
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, request.ProjectId, Permission.Write, ct))
            return Forbid();

        var result = await service.CreateRepositoryAsync(request, ct);
        return CreatedAtAction(nameof(GetRepository), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<GitLightRepoDto>> UpdateRepository(int id, [FromBody] UpdateGitLightRepoRequest request, CancellationToken ct)
    {
        var (ok, fail) = await CheckRepoAccessAsync(id, Permission.Write, ct);
        if (!ok) return fail!;

        var result = await service.UpdateRepositoryAsync(id, request, ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteRepository(int id, CancellationToken ct)
    {
        var (ok, fail) = await CheckRepoAccessAsync(id, Permission.Admin, ct);
        if (!ok) return fail!;

        var deleted = await service.DeleteRepositoryAsync(id, ct);
        if (!deleted) return NotFound();
        return NoContent();
    }

    // ── Commits ──────────────────────────────────────────────────────

    [HttpGet("{repoId:int}/commits")]
    public async Task<ActionResult<PaginatedResult<GitLightCommitDto>>> GetCommits(
        int repoId, [FromQuery, StringLength(255)] string? @ref, [FromQuery, StringLength(200)] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 30, CancellationToken ct = default)
    {
        var (ok, fail) = await CheckRepoAccessAsync(repoId, Permission.Read, ct);
        if (!ok) return fail!;

        page = Math.Max(page, 1);
        pageSize = PaginationDefaults.Clamp(pageSize);
        return Ok(await service.GetCommitsAsync(repoId, @ref, page, pageSize, search, ct));
    }

    [HttpGet("{repoId:int}/commits/{sha}")]
    public async Task<ActionResult<GitLightCommitDetailDto>> GetCommitDetail(
        int repoId, [StringLength(64, MinimumLength = 4)] string sha, CancellationToken ct = default)
    {
        var (ok, fail) = await CheckRepoAccessAsync(repoId, Permission.Read, ct);
        if (!ok) return fail!;

        var detail = await service.GetCommitDetailAsync(repoId, sha, ct);
        return detail is null ? NotFound() : Ok(detail);
    }

    // ── Branches ─────────────────────────────────────────────────────

    [HttpGet("{repoId:int}/branches")]
    public async Task<ActionResult<PaginatedResult<GitLightBranchDto>>> GetBranches(
        int repoId, [FromQuery] PaginationRequest request, CancellationToken ct)
    {
        var (ok, fail) = await CheckRepoAccessAsync(repoId, Permission.Read, ct);
        if (!ok) return fail!;
        return Ok(GitLightCollectionPager.Branches(
            await service.GetBranchesAsync(repoId, ct), request));
    }

    [HttpPost("{repoId:int}/branches")]
    public async Task<IActionResult> CreateBranch(int repoId, [FromBody] CreateGitLightBranchRequest request, CancellationToken ct)
    {
        var (ok, fail) = await CheckRepoAccessAsync(repoId, Permission.Write, ct);
        if (!ok) return fail!;
        await service.CreateBranchAsync(repoId, request, ct);
        return NoContent();
    }

    [HttpDelete("{repoId:int}/branches/{name}")]
    public async Task<IActionResult> DeleteBranch(int repoId, string name, CancellationToken ct)
    {
        var (ok, fail) = await CheckRepoAccessAsync(repoId, Permission.Write, ct);
        if (!ok) return fail!;
        await service.DeleteBranchAsync(repoId, name, ct);
        return NoContent();
    }

    // ── Tags ─────────────────────────────────────────────────────────

    [HttpGet("{repoId:int}/tags")]
    public async Task<ActionResult<PaginatedResult<GitLightTagDto>>> GetTags(
        int repoId, [FromQuery] PaginationRequest request, CancellationToken ct)
    {
        var (ok, fail) = await CheckRepoAccessAsync(repoId, Permission.Read, ct);
        if (!ok) return fail!;
        return Ok(GitLightCollectionPager.Tags(
            await service.GetTagsAsync(repoId, ct), request));
    }

    [HttpPost("{repoId:int}/tags")]
    public async Task<IActionResult> CreateTag(int repoId, [FromBody] CreateGitLightTagRequest request, CancellationToken ct)
    {
        var (ok, fail) = await CheckRepoAccessAsync(repoId, Permission.Write, ct);
        if (!ok) return fail!;
        await service.CreateTagAsync(repoId, request, ct);
        return NoContent();
    }

    [HttpDelete("{repoId:int}/tags/{name}")]
    public async Task<IActionResult> DeleteTag(int repoId, string name, CancellationToken ct)
    {
        var (ok, fail) = await CheckRepoAccessAsync(repoId, Permission.Write, ct);
        if (!ok) return fail!;
        await service.DeleteTagAsync(repoId, name, ct);
        return NoContent();
    }

    // ── File browser ─────────────────────────────────────────────────

    [HttpGet("{repoId:int}/tree")]
    public async Task<ActionResult<PaginatedResult<GitLightTreeEntryDto>>> GetTree(
        int repoId, [FromQuery, StringLength(255)] string? @ref,
        [FromQuery, StringLength(1024)] string? path,
        [FromQuery] PaginationRequest request, CancellationToken ct)
    {
        var (ok, fail) = await CheckRepoAccessAsync(repoId, Permission.Read, ct);
        if (!ok) return fail!;
        return Ok(GitLightCollectionPager.Tree(
            await service.GetTreeAsync(repoId, @ref, path, ct), request));
    }

    [HttpGet("{repoId:int}/blob")]
    public async Task<ActionResult<GitLightBlobDto>> GetBlob(
        int repoId, [FromQuery, StringLength(255)] string @ref, [FromQuery, StringLength(1024)] string path, CancellationToken ct)
    {
        var (ok, fail) = await CheckRepoAccessAsync(repoId, Permission.Read, ct);
        if (!ok) return fail!;
        var result = await service.GetBlobAsync(repoId, @ref, path, ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    [HttpGet("{repoId:int}/blob/raw")]
    public async Task<IActionResult> GetBlobRaw(
        int repoId, [FromQuery, StringLength(255)] string @ref, [FromQuery, StringLength(1024)] string path, CancellationToken ct)
    {
        var (ok, fail) = await CheckRepoAccessAsync(repoId, Permission.Read, ct);
        if (!ok) return fail!;
        var stream = await service.GetBlobStreamAsync(repoId, @ref, path, ct);
        if (stream is null) return NotFound();
        return new FileStreamResult(stream, "application/octet-stream");
    }

    // ── Internal Pull Requests ───────────────────────────────────────

    [HttpGet("{repoId:int}/pull-requests")]
    public async Task<ActionResult<PaginatedResult<InternalPullRequestDto>>> GetPullRequests(
        int repoId, [FromQuery] PullRequestPaginationRequest request, CancellationToken ct)
    {
        var (ok, fail) = await CheckRepoAccessAsync(repoId, Permission.Read, ct);
        if (!ok) return fail!;
        return Ok(await service.GetPullRequestsAsync(repoId, request, ct));
    }

    [HttpGet("{repoId:int}/pull-requests/{prNumber:int}")]
    public async Task<ActionResult<InternalPullRequestDto>> GetPullRequest(int repoId, int prNumber, CancellationToken ct)
    {
        var (ok, fail) = await CheckRepoAccessAsync(repoId, Permission.Read, ct);
        if (!ok) return fail!;
        var result = await service.GetPullRequestAsync(repoId, prNumber, ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    [HttpPost("{repoId:int}/pull-requests")]
    public async Task<ActionResult<InternalPullRequestDto>> CreatePullRequest(
        int repoId, [FromBody] CreateInternalPullRequestRequest request, CancellationToken ct)
    {
        var (ok, fail) = await CheckRepoAccessAsync(repoId, Permission.Write, ct);
        if (!ok) return fail!;
        var authorLogin = User.Identity?.Name ?? "unknown";
        var result = await service.CreatePullRequestAsync(repoId, request, authorLogin, ct);
        return CreatedAtAction(nameof(GetPullRequest), new { repoId, prNumber = result.Number }, result);
    }

    [HttpPost("{repoId:int}/pull-requests/{prNumber:int}/merge")]
    public async Task<ActionResult<InternalPullRequestDto>> MergePullRequest(int repoId, int prNumber, CancellationToken ct)
    {
        var (ok, fail) = await CheckRepoAccessAsync(repoId, Permission.Write, ct);
        if (!ok) return fail!;
        var authorLogin = User.Identity?.Name ?? "unknown";
        var result = await service.MergePullRequestAsync(repoId, prNumber, authorLogin, ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    [HttpPost("{repoId:int}/pull-requests/{prNumber:int}/close")]
    public async Task<ActionResult<InternalPullRequestDto>> ClosePullRequest(int repoId, int prNumber, CancellationToken ct)
    {
        var (ok, fail) = await CheckRepoAccessAsync(repoId, Permission.Write, ct);
        if (!ok) return fail!;
        var result = await service.ClosePullRequestAsync(repoId, prNumber, ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    [HttpGet("{repoId:int}/pull-requests/{prNumber:int}/diff")]
    public async Task<ActionResult<PullRequestDiffDto>> GetPullRequestDiff(int repoId, int prNumber, CancellationToken ct)
    {
        var (ok, fail) = await CheckRepoAccessAsync(repoId, Permission.Read, ct);
        if (!ok) return fail!;
        return Ok(await service.GetPullRequestDiffAsync(repoId, prNumber, ct));
    }

    // ── Blame ─────────────────────────────────────────────────────────

    [HttpGet("{repoId:int}/blame")]
    public async Task<ActionResult<List<GitLightBlameLine>>> GetBlame(
        int repoId, [FromQuery, StringLength(255)] string @ref, [FromQuery, StringLength(1024)] string path, CancellationToken ct)
    {
        var (ok, fail) = await CheckRepoAccessAsync(repoId, Permission.Read, ct);
        if (!ok) return fail!;
        return Ok(await service.GetBlameAsync(repoId, @ref, path, ct));
    }

    // ── Commit Graph ─────────────────────────────────────────────────

    [HttpGet("{repoId:int}/graph")]
    public async Task<ActionResult<string>> GetCommitGraph(int repoId, [FromQuery] int maxCount = 100, CancellationToken ct = default)
    {
        var (ok, fail) = await CheckRepoAccessAsync(repoId, Permission.Read, ct);
        if (!ok) return fail!;
        return Ok(await service.GetCommitGraphAsync(repoId, Math.Clamp(maxCount, 1, 1000), ct));
    }

    [HttpGet("{repoId:int}/graph-data")]
    public async Task<ActionResult<List<GitLightCommitDto>>> GetCommitGraphData(int repoId, [FromQuery] int maxCount = 120, CancellationToken ct = default)
    {
        var (ok, fail) = await CheckRepoAccessAsync(repoId, Permission.Read, ct);
        if (!ok) return fail!;
        // A graph is rendered as one connected SVG and is therefore intentionally bounded,
        // rather than paged into disconnected lane fragments.
        return Ok(await service.GetCommitGraphDataAsync(repoId, Math.Clamp(maxCount, 1, 120), ct));
    }

    // ── Branch Protection Rules ──────────────────────────────────────

    [HttpGet("{repoId:int}/branch-protection")]
    public async Task<ActionResult<PaginatedResult<BranchProtectionRuleDto>>> GetBranchProtectionRules(
        int repoId, [FromQuery] PaginationRequest request, CancellationToken ct)
    {
        var (ok, fail) = await CheckRepoAccessAsync(repoId, Permission.Read, ct);
        if (!ok) return fail!;
        return Ok(GitLightCollectionPager.Protection(
            await service.GetBranchProtectionRulesAsync(repoId, ct), request));
    }

    [HttpPost("{repoId:int}/branch-protection")]
    public async Task<ActionResult<BranchProtectionRuleDto>> CreateBranchProtectionRule(
        int repoId, [FromBody] CreateBranchProtectionRuleRequest request, CancellationToken ct)
    {
        var (ok, fail) = await CheckRepoAccessAsync(repoId, Permission.Admin, ct);
        if (!ok) return fail!;
        return Ok(await service.CreateBranchProtectionRuleAsync(repoId, request, ct));
    }

    [HttpPut("{repoId:int}/branch-protection/{ruleId:int}")]
    public async Task<ActionResult<BranchProtectionRuleDto>> UpdateBranchProtectionRule(
        int repoId, int ruleId, [FromBody] UpdateBranchProtectionRuleRequest request, CancellationToken ct)
    {
        var (ok, fail) = await CheckRepoAccessAsync(repoId, Permission.Admin, ct);
        if (!ok) return fail!;
        var result = await service.UpdateBranchProtectionRuleAsync(repoId, ruleId, request, ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    [HttpDelete("{repoId:int}/branch-protection/{ruleId:int}")]
    public async Task<IActionResult> DeleteBranchProtectionRule(int repoId, int ruleId, CancellationToken ct)
    {
        var (ok, fail) = await CheckRepoAccessAsync(repoId, Permission.Admin, ct);
        if (!ok) return fail!;
        var deleted = await service.DeleteBranchProtectionRuleAsync(ruleId, ct);
        if (!deleted) return NotFound();
        return NoContent();
    }
}
