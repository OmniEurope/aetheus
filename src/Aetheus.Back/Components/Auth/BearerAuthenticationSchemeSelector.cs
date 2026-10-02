// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Authorization;

namespace Aetheus.Back.Components.Auth;

internal static class BearerAuthenticationSchemeSelector
{
    public static string? Resolve(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        const string bearerPrefix = "Bearer ";
        if (!header.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)) return null;

        var token = header[bearerPrefix.Length..].Trim();
        if (token.StartsWith(PatConstants.TokenPrefix, StringComparison.Ordinal))
            return PatConstants.SchemeName;

        var requiresAgent = context.GetEndpoint()?.Metadata
            .GetOrderedMetadata<IAuthorizeData>()
            .Any(data => string.Equals(
                data.Policy,
                AgentTokenAuthenticationHandler.SchemeName,
                StringComparison.Ordinal)) == true;

        return requiresAgent ? AgentTokenAuthenticationHandler.SchemeName : null;
    }
}
