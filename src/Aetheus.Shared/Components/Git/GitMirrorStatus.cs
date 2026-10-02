// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.Git;

/// <summary>
/// Lifecycle state of a server-side bare mirror backing an external Git repository.
/// </summary>
public enum GitMirrorStatus
{
    /// <summary>No mirror cloned yet.</summary>
    NotInitialized = 0,

    /// <summary>A clone/fetch is currently running.</summary>
    Syncing = 1,

    /// <summary>The mirror is cloned and the last fetch succeeded.</summary>
    Ready = 2,

    /// <summary>The last clone/fetch failed (see <c>LastFetchError</c>).</summary>
    Error = 3
}
