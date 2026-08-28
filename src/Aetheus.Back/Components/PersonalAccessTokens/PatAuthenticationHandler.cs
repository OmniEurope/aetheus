// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Aetheus.Back.Components.PersonalAccessTokens;

/// <summary>
/// Authenticates a request bearing a Personal Access Token (ADR-024 4.5). Reached from the JWT
/// bearer handler's <c>ForwardDefaultSelector</c> when the <c>Authorization: Bearer</c> value carries
/// the <see cref="PatConstants.TokenPrefix"/>. Emits the SAME claim shape as a JWT session (Name /
/// NameIdentifier / Role) plus the PAT scope + id, so every existing <c>[Authorize]</c> endpoint and
/// RBAC check works transparently - and the scope can never exceed the user's live roles.
/// </summary>
internal sealed class PatAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IPersonalAccessTokenService patService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = PatConstants.SchemeName;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!AuthorizationHeaderParser.TryGetCredentials(Request, "Bearer", out var token))
            return AuthenticateResult.NoResult();
        if (!token.StartsWith(PatConstants.TokenPrefix, StringComparison.Ordinal))
            return AuthenticateResult.NoResult();

        var principal = await patService.ValidateAsync(token, Context.RequestAborted).ConfigureAwait(false);
        if (principal is null)
            return AuthenticateResult.Fail("Invalid or expired personal access token.");

        var scopeValue = principal.Scope == PatScope.ReadOnly
            ? PatConstants.ReadOnlyScopeValue
            : PatConstants.ReadWriteScopeValue;

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, principal.Username),
            new(ClaimTypes.NameIdentifier, principal.UserId.ToString()),
            new(PatConstants.ScopeClaimType, scopeValue),
            new(PatConstants.TokenIdClaimType, principal.TokenId.ToString())
        };
        foreach (var role in principal.Roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return AuthenticateResult.Success(ticket);
    }
}
