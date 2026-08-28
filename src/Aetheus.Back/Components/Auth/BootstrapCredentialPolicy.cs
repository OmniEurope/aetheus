// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Auth;

/// <summary>Which bootstrap identity a login request satisfied, and how long its token may live.</summary>
internal sealed record BootstrapMatch(DateTimeOffset? ExpiresAt);

/// <summary>
/// Decides whether a login request matches one of the two bootstrap identities. It answers a
/// question; issuing the token stays with <see cref="AuthService"/>.
///
/// There are two identities and they are deliberately separate:
///
///   the persistent administrator (Auth:AdminUser / Auth:AdminPassword) comes from the environment
///   file and is usable only until the database holds a user - which is the moment DbInitializer has
///   seeded it from that same password and a real login can take over;
///
///   the deployment identity (Auth:DeploymentBootstrapUser / ...Password) is derived per run, is
///   self-expiring, and exists so a deployment smoke can exercise real authentication without
///   changing or learning a persisted user's password.
///
/// They used to share the ADMIN_* names. Because DbInitializer hashes Auth:AdminPassword into the
/// seeded `admin`, deploying to a NEW environment then gave it a permanent administrator whose
/// password was that run's throwaway secret - one the run masks out of its own log by design. The
/// environment came up healthy and nobody could ever log into it again.
/// </summary>
internal static class BootstrapCredentialPolicy
{
    internal static BootstrapMatch? Match(
        LoginRequest request, IConfiguration config, TimeProvider timeProvider, bool hasDbUsers)
    {
        var deploymentExpiresAt = DeploymentBootstrapIdentity.GetActiveExpiration(config, timeProvider);
        if (hasDbUsers && deploymentExpiresAt is null) return null;

        if (deploymentExpiresAt is not null)
        {
            var deployUser = config["Auth:DeploymentBootstrapUser"];
            var deployPass = config["Auth:DeploymentBootstrapPassword"];
            if (!string.IsNullOrEmpty(deployUser) && !string.IsNullOrEmpty(deployPass)
                && DeploymentBootstrapIdentity.CredentialsMatch(request, deployUser, deployPass))
            {
                return new BootstrapMatch(deploymentExpiresAt);
            }
        }

        if (hasDbUsers) return null;

        var adminUser = config["Auth:AdminUser"] ?? "admin";
        var adminPass = config["Auth:AdminPassword"];
        if (string.IsNullOrEmpty(adminPass)
            || !DeploymentBootstrapIdentity.CredentialsMatch(request, adminUser, adminPass))
            return null;

        // The seeded administrator is NOT the deployment identity: it authenticated with
        // Auth:AdminPassword on an empty database. Carrying the deployment window here tied an
        // unrelated login to a deployment's clock, so this branch expires on its own terms.
        return new BootstrapMatch(null);
    }
}
