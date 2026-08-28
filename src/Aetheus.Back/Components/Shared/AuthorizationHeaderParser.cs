// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Shared;

internal static class AuthorizationHeaderParser
{
    public static bool TryGetCredentials(HttpRequest request, string scheme, out string credentials)
    {
        credentials = string.Empty;
        if (!request.Headers.TryGetValue("Authorization", out var authorization)) return false;

        var header = authorization.ToString();
        var prefix = scheme + " ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

        credentials = header[prefix.Length..].Trim();
        return true;
    }
}
