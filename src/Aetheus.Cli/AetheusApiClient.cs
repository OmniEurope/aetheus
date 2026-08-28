// SPDX-License-Identifier: EUPL-1.2
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;

namespace Aetheus.Cli;

// Composition over inheritance - F48: do not inherit from HttpClient.
internal sealed class AetheusApiClient : IDisposable
{
    private readonly HttpClient _http;
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    internal AetheusApiClient(Uri server, string? token)
    {
        var insecureOverride = Environment.GetEnvironmentVariable("AETHEUS_INSECURE") == "1";
        if (!string.IsNullOrEmpty(token) && server.Scheme == Uri.UriSchemeHttp && !server.IsLoopback)
        {
            if (!insecureOverride || !IsPrivateIpLiteral(server.Host))
            {
                throw new ArgumentException(
                    "Refusing to send a bearer token over plaintext HTTP. AETHEUS_INSECURE=1 "
                    + "is restricted to private or link-local IP literals; use HTTPS otherwise.");
            }
            Console.Error.WriteLine(
                "WARNING: AETHEUS_INSECURE=1 is sending a bearer token over plaintext HTTP; "
                + "traffic and credentials can be intercepted.");
        }

        _http = new HttpClient { BaseAddress = server, Timeout = RequestTimeout };
        if (!string.IsNullOrEmpty(token))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static bool IsPrivateIpLiteral(string host)
    {
        if (!IPAddress.TryParse(host, out var address))
            return false;
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10
                || bytes[0] == 127
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254);
        }

        return IPAddress.IsLoopback(address)
            || address.IsIPv6LinkLocal
            || (address.GetAddressBytes()[0] & 0xFE) == 0xFC;
    }

    internal Task<T?> GetFromJsonAsync<T>(string requestUri, CancellationToken ct) =>
        _http.GetFromJsonAsync<T>(requestUri, ct);

    internal Task<HttpResponseMessage> PostAsync(string requestUri, HttpContent? content, CancellationToken ct) =>
        _http.PostAsync(requestUri, content, ct);

    internal Task<HttpResponseMessage> GetAsync(string requestUri, CancellationToken ct) =>
        _http.GetAsync(requestUri, ct);

    public void Dispose() => _http.Dispose();
}
