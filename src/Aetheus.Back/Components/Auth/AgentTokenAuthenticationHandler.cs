// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Aetheus.Back.Components.Auth;

public class AgentTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IAuthService authService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "AgentToken";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!AuthorizationHeaderParser.TryGetCredentials(Request, "Bearer", out var token))
            return AuthenticateResult.NoResult();
        if (string.IsNullOrEmpty(token))
            return AuthenticateResult.Fail("Empty token.");

        var serverId = await authService.ValidateServerTokenAsync(token, Context.RequestAborted).ConfigureAwait(false);
        if (serverId is null)
            return AuthenticateResult.Fail("Invalid agent token.");

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, serverId.Value.ToString()),
            new Claim("ServerId", serverId.Value.ToString()),
            new Claim(ClaimTypes.Role, "Agent")
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return AuthenticateResult.Success(ticket);
    }
}
