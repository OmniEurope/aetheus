// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Components.ServiceConnections;

/// <summary>
/// Probes a service connection against its real provider. Uses the SSRF-guarded
/// "service-connection-test" HttpClient so a malicious Url cannot reach internal hosts.
/// </summary>
public sealed class ServiceConnectionTester(IHttpClientFactory httpClientFactory, ILogger<ServiceConnectionTester> logger)
    : IServiceConnectionTester
{
    public async Task<ServiceConnectionTestResultDto> TestAsync(
        ServiceConnectionType type, string? url, string configurationJson, CancellationToken ct = default)
    {
        var creds = ParseCredentials(configurationJson);

        // Refuse plaintext http: the probe attaches the token / Basic credentials, which would then travel
        // in the clear to an external host. https is required for any explicitly provided URL.
        if (!string.IsNullOrWhiteSpace(url)
            && Uri.TryCreate(url, UriKind.Absolute, out var provided)
            && provided.Scheme == Uri.UriSchemeHttp)
        {
            return new ServiceConnectionTestResultDto
            {
                Status = ServiceConnectionTestStatus.Invalid,
                Message = "Use an https:// URL - plaintext http would send the credentials in the clear."
            };
        }

        try
        {
            return type switch
            {
                ServiceConnectionType.GitHub => await ProbeGitHubAsync(url, creds, ct).ConfigureAwait(false),
                ServiceConnectionType.GitLab => await ProbeGitLabAsync(url, creds, ct).ConfigureAwait(false),
                ServiceConnectionType.DockerRegistry => await ProbeDockerAsync(url, creds, ct).ConfigureAwait(false),
                _ => Unsupported(type)
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException)
        {
            // Transport failure, timeout, blocked (SSRF), or malformed Url: honest error, never a green.
            // Log only scheme+host (never the raw Url, which may embed userinfo credentials).
            logger.LogWarning(ex, "Service connection probe failed for {Type} ({Host})", type, SafeHost(url));
            return new ServiceConnectionTestResultDto
            {
                Status = ServiceConnectionTestStatus.Error,
                Message = "Could not reach the provider (unreachable, timed out, or blocked)."
            };
        }
    }

    private async Task<ServiceConnectionTestResultDto> ProbeGitHubAsync(string? url, Credentials creds, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(creds.Token))
            return Invalid("No access token configured for this GitHub connection.");

        // github.com -> api.github.com/user ; GitHub Enterprise host -> {origin}/api/v3/user.
        var apiBase = ResolveGitHubApiBase(url);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{apiBase}/user");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", creds.Token);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await Client().SendAsync(request, ct).ConfigureAwait(false);
        return MapAuthResponse(response.StatusCode, "GitHub");
    }

    private async Task<ServiceConnectionTestResultDto> ProbeGitLabAsync(string? url, Credentials creds, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(creds.Token))
            return Invalid("No access token configured for this GitLab connection.");

        var origin = ResolveOrigin(url) ?? "https://gitlab.com";
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{origin}/api/v4/user");
        request.Headers.Add("PRIVATE-TOKEN", creds.Token);

        using var response = await Client().SendAsync(request, ct).ConfigureAwait(false);
        return MapAuthResponse(response.StatusCode, "GitLab");
    }

    private async Task<ServiceConnectionTestResultDto> ProbeDockerAsync(string? url, Credentials creds, CancellationToken ct)
    {
        var origin = ResolveOrigin(url);
        if (origin is null)
            return Invalid("A registry URL is required to test a Docker registry connection.");

        // The Registry v2 base endpoint returns 200 when the credentials authenticate against it.
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{origin}/v2/");
        if (!string.IsNullOrEmpty(creds.Username))
        {
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{creds.Username}:{creds.Password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        }

        using var response = await Client().SendAsync(request, ct).ConfigureAwait(false);
        return MapAuthResponse(response.StatusCode, "Docker registry");
    }

    private HttpClient Client() => httpClientFactory.CreateClient("service-connection-test");

    private static ServiceConnectionTestResultDto MapAuthResponse(HttpStatusCode status, string provider) => status switch
    {
        HttpStatusCode.OK or HttpStatusCode.NoContent => new ServiceConnectionTestResultDto
        {
            Status = ServiceConnectionTestStatus.Valid,
            Message = $"Authenticated successfully against {provider}."
        },
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new ServiceConnectionTestResultDto
        {
            Status = ServiceConnectionTestStatus.Invalid,
            Message = $"{provider} rejected the credentials (they may be wrong or expired)."
        },
        _ => new ServiceConnectionTestResultDto
        {
            Status = ServiceConnectionTestStatus.Error,
            Message = $"{provider} returned an unexpected status ({(int)status})."
        }
    };

    private static ServiceConnectionTestResultDto Unsupported(ServiceConnectionType type) => new()
    {
        Status = ServiceConnectionTestStatus.Unsupported,
        Message = $"Automated connection testing is not available for {type} connections."
    };

    private static ServiceConnectionTestResultDto Invalid(string message) => new()
    {
        Status = ServiceConnectionTestStatus.Invalid,
        Message = message
    };

    private static string ResolveGitHubApiBase(string? url)
    {
        var origin = ResolveOrigin(url);
        if (origin is null) return "https://api.github.com";
        // Public github.com uses the api. subdomain; Enterprise hosts expose /api/v3.
        return origin.Contains("github.com", StringComparison.OrdinalIgnoreCase)
            ? "https://api.github.com"
            : $"{origin}/api/v3";
    }

    // Scheme + host only, for safe logging (drops any userinfo/path/query that could carry a secret).
    private static string SafeHost(string? url) =>
        !string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out var u)
            ? $"{u.Scheme}://{u.Host}"
            : "(none)";

    private static string? ResolveOrigin(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
            return null;
        return uri.IsDefaultPort ? $"{uri.Scheme}://{uri.Host}" : $"{uri.Scheme}://{uri.Host}:{uri.Port}";
    }

    private static Credentials ParseCredentials(string configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson)) return new Credentials(null, null, null);
        try
        {
            using var doc = JsonDocument.Parse(configurationJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return new Credentials(null, null, null);
            return new Credentials(
                GetString(root, "token") ?? GetString(root, "accessToken") ?? GetString(root, "pat"),
                GetString(root, "username") ?? GetString(root, "user"),
                GetString(root, "password") ?? GetString(root, "secret"));
        }
        catch (JsonException)
        {
            return new Credentials(null, null, null);
        }
    }

    private static string? GetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private sealed record Credentials(string? Token, string? Username, string? Password);
}
