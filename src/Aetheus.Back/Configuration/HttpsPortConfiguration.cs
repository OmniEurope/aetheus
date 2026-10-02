// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Configuration;

/// <summary>
/// R-464: whether the HTTPS redirection middleware can know which port to redirect to. It reads the
/// port from <c>HTTPS_PORT</c> / <c>ANCM_HTTPS_PORT</c>, or from the server's own HTTPS address. Without
/// any, it redirects nothing and logs "Failed to determine the https port for redirect" on every plain
/// HTTP request: the production container listens on HTTP only, Apache terminates TLS in front of it.
/// These are the same sources, read from the same configuration (the <c>ASPNETCORE_</c> environment
/// variables included), so the middleware runs wherever it would find a port and nowhere else.
/// </summary>
public static class HttpsPortConfiguration
{
    public static bool IsKnown(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!string.IsNullOrWhiteSpace(configuration["HTTPS_PORT"])
            || !string.IsNullOrWhiteSpace(configuration["ANCM_HTTPS_PORT"])
            || !string.IsNullOrWhiteSpace(configuration["HTTPS_PORTS"]))
            return true;
        if (IsHttpsUrlList(configuration["URLS"]))
            return true;
        return configuration.GetSection("Kestrel:Endpoints").GetChildren()
            .Any(endpoint => IsHttpsUrlList(endpoint["Url"]));
    }

    private static bool IsHttpsUrlList(string? urls) =>
        !string.IsNullOrWhiteSpace(urls)
        && urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(url => url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
}
