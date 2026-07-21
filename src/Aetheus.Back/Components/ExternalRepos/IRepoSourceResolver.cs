// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.ExternalRepos;

/// <summary>
/// The effective clone source a pipeline should use for a project. For an external (mirror-backed)
/// project this is the mirror's internal smart-HTTP URL - identical in shape to an internal repo -
/// so the agent never sees the external write credentials (Fork B: forward via backend).
/// </summary>
public sealed record RepoSource(string? CloneUrl, string? DefaultBranch, bool IsExternal, bool IsMirrorBacked);

/// <summary>
/// Resolves the clone source for a project's pipelines. The single authority that makes an external
/// repository indistinguishable from an internal one at the consumption layer.
/// </summary>
public interface IRepoSourceResolver
{
    Task<RepoSource> ResolveAsync(int projectId, CancellationToken ct = default);
}
