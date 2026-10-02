// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Git;

/// <summary>
/// Fetches the mirror behind a repository of a project, when that repository is the mirror of an
/// external one (its connection is not an Aetheus-hosted one).
/// </summary>
public sealed class ExternalMirrorRefresher(
    IGitLightRepository repositories,
    IExternalRepoMirrorService mirror) : IExternalMirrorRefresher
{
    public async Task<string?> RefreshAsync(int projectId, string slug, CancellationToken ct = default)
    {
        var repository = await repositories.FindBySlugAsync(projectId, slug, ct).ConfigureAwait(false);
        if (repository?.GitConnection is not { ProviderType: not GitProviderType.AetheusGit } connection)
        {
            // Not a mirror (the project's own repository, or an unknown slug the caller reports): nothing to fetch.
            return null;
        }

        var status = await mirror.SyncAsync(connection, repository, ct).ConfigureAwait(false);
        await repositories.SaveChangesAsync(ct).ConfigureAwait(false);
        return status == GitMirrorStatus.Ready
            ? null
            : string.IsNullOrWhiteSpace(connection.LastFetchError) ? "the remote could not be fetched." : connection.LastFetchError;
    }
}
