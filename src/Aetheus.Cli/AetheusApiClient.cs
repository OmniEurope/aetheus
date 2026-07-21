// SPDX-License-Identifier: EUPL-1.2
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Aetheus.Cli;

// Composition over inheritance - F48: do not inherit from HttpClient.
public sealed class AetheusApiClient : IDisposable
{
    private readonly HttpClient _http;

    public AetheusApiClient(string server, string? token)
    {
        ArgumentException.ThrowIfNullOrEmpty(server);

        // Hardening (#44): refuse to send a bearer token over plaintext HTTP. CLI users that
        // explicitly target a non-loopback HTTP endpoint with a token would leak it on the wire.
        // Loopback addresses (localhost / 127.x / ::1) are exempt for development.
        var uri = new Uri(server);
        var insecureOverride = Environment.GetEnvironmentVariable("AETHEUS_INSECURE") == "1";
        if (!string.IsNullOrEmpty(token) && uri.Scheme == Uri.UriSchemeHttp && !IsLoopback(uri))
            throw new InvalidOperationException(
                "Refusing to send bearer token over plaintext HTTP. Use https:// or set AETHEUS_INSECURE=1.");
        if (!string.IsNullOrEmpty(token) && uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback && insecureOverride)
        {
            Console.Error.WriteLine(
                "WARNING: AETHEUS_INSECURE=1 is sending a bearer token over plaintext HTTP; "
                + "traffic and credentials can be intercepted.");
        }

        _http = new HttpClient { BaseAddress = uri };
        if (!string.IsNullOrEmpty(token))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static bool IsLoopback(Uri uri)
    {
        if (Environment.GetEnvironmentVariable("AETHEUS_INSECURE") == "1") return true;
        return uri.IsLoopback;
    }

    public Task<T?> GetFromJsonAsync<T>(string requestUri, CancellationToken ct = default) =>
        _http.GetFromJsonAsync<T>(requestUri, ct);

    public Task<HttpResponseMessage> PostAsync(string requestUri, HttpContent? content, CancellationToken ct = default) =>
        _http.PostAsync(requestUri, content, ct);

    public Task<HttpResponseMessage> GetAsync(string requestUri, CancellationToken ct = default) =>
        _http.GetAsync(requestUri, ct);

    public void Dispose() => _http.Dispose();
}
