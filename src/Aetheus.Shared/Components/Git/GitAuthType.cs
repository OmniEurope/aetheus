// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.Git;

/// <summary>
/// Authentication scheme used by an external Git connection.
/// </summary>
public enum GitAuthType
{
    /// <summary>HTTPS with a Personal Access Token (injected via <c>http.extraHeader</c>).</summary>
    HttpsToken = 0,

    /// <summary>SSH with a private deploy key and pinned <c>known_hosts</c>.</summary>
    Ssh = 1
}
