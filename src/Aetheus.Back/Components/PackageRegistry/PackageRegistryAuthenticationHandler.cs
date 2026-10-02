// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Aetheus.Back.Components.Auth;
using Aetheus.Back.Components.PersonalAccessTokens;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Aetheus.Back.Components.PackageRegistry;

internal sealed class PackageRegistryAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IPersonalAccessTokenService personalAccessTokens)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "PackageRegistry";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = ReadToken();
        if (token is null)
            return AuthenticateResult.NoResult();
        if (!token.StartsWith(PatConstants.TokenPrefix, StringComparison.Ordinal))
            return AuthenticateResult.Fail("The package registry requires an Aetheus personal access token.");

        var principal = await personalAccessTokens.ValidateAsync(token, Context.RequestAborted).ConfigureAwait(false);
        if (principal is null)
            return AuthenticateResult.Fail("Invalid or expired personal access token.");

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, principal.Username),
            new(ClaimTypes.NameIdentifier, principal.UserId.ToString()),
            new(PatConstants.ScopeClaimType,
                principal.Scope == PatScope.ReadOnly
                    ? PatConstants.ReadOnlyScopeValue
                    : PatConstants.ReadWriteScopeValue),
            new(PatConstants.TokenIdClaimType, principal.TokenId.ToString())
        };
        claims.AddRange(principal.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        var identity = new ClaimsIdentity(claims, SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Basic realm=\"Aetheus Package Registry\", Bearer";
        return Task.CompletedTask;
    }

    private string? ReadToken()
    {
        var apiKey = Request.Headers["X-NuGet-ApiKey"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(apiKey)
            && apiKey.Trim().StartsWith(PatConstants.TokenPrefix, StringComparison.Ordinal))
            return apiKey.Trim();

        var authorization = Request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return authorization["Bearer ".Length..].Trim();
        if (!authorization.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return null;

        var encoded = authorization["Basic ".Length..].Trim();
        if (encoded.Length is < 1 or > 16_384)
            return null;
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            var separator = decoded.IndexOf(':');
            return separator >= 0 ? decoded[(separator + 1)..] : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
