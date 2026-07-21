// SPDX-License-Identifier: EUPL-1.2
using System.Data.Common;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Aetheus.Back.Components.PersonalAccessTokens;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Aetheus.Back.Components.Git;

/// <summary>
/// HTTP Basic authentication handler for the Git Smart HTTP endpoints. The git CLI does not
/// speak JWT, so we implement a dedicated scheme that:
/// <list type="bullet">
///   <item>parses the standard <c>Authorization: Basic &lt;base64&gt;</c> header,</item>
///   <item>validates credentials via <see cref="IGitSmartHttpService.ValidateBasicAuthAsync"/>,</item>
///   <item>emits a <c>WWW-Authenticate: Basic realm="Aetheus Git"</c> challenge on 401.</item>
/// </list>
/// Wiring: registered alongside JWT/Agent token schemes in <c>Program.cs</c>; the controller
/// activates it via <c>[Authorize(AuthenticationSchemes = GitBasicAuthenticationHandler.SchemeName)]</c>.
/// </summary>
public class GitBasicAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IGitSmartHttpService smartHttp,
    IPersonalAccessTokenService personalAccessTokens,
    IConfiguration configuration,
    TimeProvider timeProvider)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "GitBasic";

    /// <summary>Claim carrying the project a pipeline-run clone token is scoped to. The smart-HTTP
    /// controller honours it for read access without a per-user permission lookup.</summary>
    public const string RunScopeClaim = "git_run_scope";

    private const string Realm = "Aetheus Git";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
            return AuthenticateResult.NoResult();

        var header = authHeader.ToString();
        if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        string username;
        string password;
        try
        {
            var encoded = header["Basic ".Length..].Trim();
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            var colonIndex = decoded.IndexOf(':');
            if (colonIndex < 0)
                return AuthenticateResult.Fail("Malformed Basic credentials.");

            username = decoded[..colonIndex];
            password = decoded[(colonIndex + 1)..];
        }
        catch (FormatException)
        {
            return AuthenticateResult.Fail("Invalid Base64 in Authorization header.");
        }

        // Pipeline-run clone token: stateless, project-scoped, self-expiring (see GitRunCloneToken).
        // Validated against the route's projectId so a token can only clone the repo it was minted for,
        // then gated on the run still being active so a token leaked after the run ends is inert before
        // its 6h HMAC expiry.
        if (username.StartsWith(GitRunCloneToken.UsernamePrefix, StringComparison.Ordinal))
        {
            if (TryGetRouteProjectId(out var routeProjectId)
                && GitRunCloneToken.Validate(configuration, username, password, routeProjectId,
                    timeProvider.GetUtcNow().UtcDateTime) is { } tokenRunId
                && await IsRunActiveOrDbDegradedAsync(tokenRunId).ConfigureAwait(false))
            {
                var runIdentity = new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.Name, username),
                        new Claim(RunScopeClaim, routeProjectId.ToString()),
                    ], SchemeName);
                return AuthenticateResult.Success(
                    new AuthenticationTicket(new ClaimsPrincipal(runIdentity), SchemeName));
            }
            return AuthenticateResult.Fail("Invalid or out-of-scope Git run token.");
        }

        // Personal access tokens are the non-interactive credential for Git clients.  The HTTP
        // Basic username is retained so standard git tooling can use `username:PAT`; bind it to
        // the token owner so a copied token cannot masquerade as another account in audit trails.
        if (password.StartsWith(PatConstants.TokenPrefix, StringComparison.Ordinal))
        {
            var patPrincipal = await personalAccessTokens.ValidateAsync(password, Context.RequestAborted)
                .ConfigureAwait(false);
            if (patPrincipal is null
                || !string.Equals(username, patPrincipal.Username, StringComparison.OrdinalIgnoreCase))
            {
                return AuthenticateResult.Fail("Invalid Git personal access token.");
            }

            var scope = patPrincipal.Scope == PatScope.ReadOnly
                ? PatConstants.ReadOnlyScopeValue
                : PatConstants.ReadWriteScopeValue;
            var patClaims = new List<Claim>
            {
                new(ClaimTypes.Name, patPrincipal.Username),
                new(ClaimTypes.NameIdentifier, patPrincipal.UserId.ToString()),
                new(PatConstants.ScopeClaimType, scope),
                new(PatConstants.TokenIdClaimType, patPrincipal.TokenId.ToString())
            };
            patClaims.AddRange(patPrincipal.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

            var patIdentity = new ClaimsIdentity(patClaims, SchemeName);
            return AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(patIdentity), SchemeName));
        }

        var valid = await smartHttp.ValidateBasicAuthAsync(username, password, Context.RequestAborted)
            .ConfigureAwait(false);
        if (!valid)
            return AuthenticateResult.Fail("Invalid Git credentials.");

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, "GitUser"),
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return AuthenticateResult.Success(ticket);
    }

    /// <summary>
    /// Liveness gate around <see cref="IGitSmartHttpService.IsRunActiveAsync"/>. The gate makes a leaked
    /// token inert as soon as its run ends, but it couples clone auth to a DB lookup on every request.
    /// A transient DB fault must NOT 500 a legitimate in-flight clone, so on error we deliberately
    /// degrade to the HMAC-only guarantee the token already passed (valid signature + unexpired +
    /// project-scoped) instead of failing closed. The window this reopens is narrow and bounded (a
    /// token that leaked AND is still within its 6h HMAC expiry AND a concurrent DB outage) and
    /// availability of legitimate clones is the deliberate winner. Cooperative cancellation still propagates.
    /// </summary>
    private async Task<bool> IsRunActiveOrDbDegradedAsync(int runId)
    {
        try
        {
            return await smartHttp.IsRunActiveAsync(runId, Context.RequestAborted).ConfigureAwait(false);
        }
        // Degrade to HMAC-only ONLY for genuine data-layer faults (DB outage / EF connection / query
        // timeout). A narrow catch keeps an unrelated code bug from being silently swallowed as a
        // "security degradation" - it surfaces as a 500 instead of widening the leaked-token window.
        catch (Exception ex) when (ex is DbException or InvalidOperationException or TimeoutException)
        {
            // Elevate to a security-grade alert (not a routine warning): every pass through this branch
            // widens the leaked-token window to the full HMAC TTL, so it must be conspicuous to monitoring
            // rather than buried in warnings. The "SEC-DEGRADE" marker is the alerting hook.
            Logger.LogError(ex,
                "SEC-DEGRADE: run-liveness lookup failed for run {RunId}; git clone auth degraded to HMAC-only "
                + "(leaked-token window reopened until HMAC expiry). Investigate the data-layer fault.", runId);
            return true;
        }
    }

    private bool TryGetRouteProjectId(out int projectId)
    {
        projectId = 0;
        return Request.RouteValues.TryGetValue("projectId", out var raw)
            && int.TryParse(raw?.ToString(), out projectId);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers["WWW-Authenticate"] = $"Basic realm=\"{Realm}\"";
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }
}
