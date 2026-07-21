// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Git;
using Aetheus.Back.Components.Projects;
using Aetheus.Back.Configuration;
using Microsoft.Extensions.Options;

namespace Aetheus.Back.Components.ExternalRepos;

public sealed class RepoSourceResolver(
    IProjectRepository projectRepo,
    IGitLightRepository lightRepo,
    IConfiguration configuration,
    IHttpContextAccessor httpContextAccessor,
    IOptions<FeatureFlagsOptions> features) : IRepoSourceResolver
{
    public async Task<RepoSource> ResolveAsync(int projectId, CancellationToken ct = default)
    {
        var project = await projectRepo.FindProjectAsync(projectId, ct).ConfigureAwait(false);
        if (project is null)
        {
            return new RepoSource(null, null, IsExternal: false, IsMirrorBacked: false);
        }

        var isExternal = features.Value.ExternalRepos && project.GitConnectionId is not null;
        if (!isExternal)
        {
            // Internal repo (or plain RepositoryUrl): clone exactly what the project points to, but
            // re-home an internal-mirror URL onto the executing backend's clone base (same fix as
            // PipelineRunService), so a containerised/remote consumer never inherits a frozen
            // localhost:5300 authority. Rehome is a no-op for any non-mirror URL.
            return new RepoSource(
                MirrorCloneUrl.Rehome(configuration, project.RepositoryUrl), project.DefaultBranch,
                IsExternal: false, IsMirrorBacked: false);
        }

        // External: clone the mirror over the internal smart-HTTP path. RepositoryUrl already holds the
        // mirror URL (set at attach), but recompute the canonical URL so a stale value never leaks the
        // external remote to the agent.
        var mirrors = await lightRepo.GetByProjectAsync(projectId, ct).ConfigureAwait(false);
        var mirror = mirrors.FirstOrDefault(r => r.GitConnectionId == project.GitConnectionId);
        var cloneUrl = mirror is not null
            ? BuildMirrorCloneUrl(projectId, mirror.Slug)
            : MirrorCloneUrl.Rehome(configuration, project.RepositoryUrl);

        return new RepoSource(cloneUrl, project.DefaultBranch, IsExternal: true, IsMirrorBacked: true);
    }

    private string BuildMirrorCloneUrl(int projectId, string slug) =>
        MirrorCloneUrl.Build(configuration, httpContextAccessor.HttpContext, projectId, slug);
}
