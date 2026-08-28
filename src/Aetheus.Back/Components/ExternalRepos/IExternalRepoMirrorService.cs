// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.ExternalRepos;

/// <summary>
/// Maintains the server-side "credited mirror" of an external repository - a bare repo kept at the
/// standard internal path so the existing browse / smart-HTTP / pipeline-clone paths consume it
/// unchanged. All credential handling is centralized here; agents never see write credentials.
/// </summary>
public interface IExternalRepoMirrorService
{
    /// <summary>
    /// Probes an external remote with <c>git ls-remote</c> before it is persisted, so registration
    /// fails fast on a bad URL / credential. Never throws on a git error - returns the masked reason.
    /// </summary>
    Task<(bool Ok, string? Error)> TestConnectivityAsync(
        GitProviderType provider, string? baseUrl, string ownerOrGroup, string repositoryName,
        GitAuthType authType, GitCredentialPayload? credential, CancellationToken ct = default);

    /// <summary>
    /// Clones (first run) or fetches (subsequent) the mirror for <paramref name="connection"/> into
    /// <paramref name="mirror"/>'s bare path, then updates the connection's mirror status. Returns the
    /// resulting status. Caller persists the entities.
    /// </summary>
    Task<GitMirrorStatus> SyncAsync(GitConnection connection, GitInternalRepo mirror, CancellationToken ct = default);

    /// <summary>Resolves the on-disk bare path of a mirror, with the path-traversal guard.</summary>
    string ResolveMirrorPath(int projectId, string slug);
}
