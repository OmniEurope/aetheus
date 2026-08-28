// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Aetheus.Front.Services;

public sealed class AuthDelegatingHandler(
    AuthStateProvider auth,
    ILogger<AuthDelegatingHandler> logger) : DelegatingHandler
{
    // Marker header used to short-circuit re-entrant renewal calls so the renew
    // request itself doesn't trigger another renewal check.
    private const string RenewalMarkerHeader = "X-Aetheus-Renewal";

    private readonly SemaphoreSlim _renewalLock = new(1, 1);
    private Task<RenewalOutcome>? _inFlightRenewal;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var isRenewalRequest = request.Headers.Contains(RenewalMarkerHeader);

        if (!isRenewalRequest && auth.ShouldRenew())
            await EnsureRenewedAsync(request, cancellationToken).ConfigureAwait(false);

        var sentToken = auth.Token;
        if (!string.IsNullOrEmpty(sentToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sentToken);

        // S-TECH-30: buffer the body up-front so the request can be re-sent verbatim if the access
        // token turns out to have expired (the 401 path below clones and retries with a fresh token).
        if (!isRenewalRequest && request.Content is not null)
            await request.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized && !isRenewalRequest &&
            !IsLoginRequest(request.RequestUri))
        {
            var endpoint = EndpointForLog(request.RequestUri);
            logger.LogWarning(
                "Authenticated request {Method} {Endpoint} returned 401; forcing token refresh",
                request.Method.Method, endpoint);

            var renewal = await EnsureRenewedAsync(
                request, cancellationToken, force: true, rejectedToken: sentToken).ConfigureAwait(false);

            if (renewal == RenewalOutcome.Succeeded && auth.HasValidToken)
            {
                logger.LogDebug("Fresh token available after 401; retrying {Method} {Endpoint} once",
                    request.Method.Method, endpoint);
                var retry = await CloneRequestAsync(request).ConfigureAwait(false);
                retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
                var retryResponse = await base.SendAsync(retry, cancellationToken).ConfigureAwait(false);
                response.Dispose();
                if (retryResponse.StatusCode != HttpStatusCode.Unauthorized)
                    return retryResponse;

                logger.LogWarning(
                    "Authenticated request {Method} {Endpoint} remained unauthorized after a successful token refresh; preserving session",
                    request.Method.Method, endpoint);
                return retryResponse;
            }

            if (renewal == RenewalOutcome.Rejected)
            {
                logger.LogWarning(
                    "Token refresh was definitively rejected after 401 from {Method} {Endpoint}; clearing session",
                    request.Method.Method, endpoint);
                await auth.LogoutAsync();
                // F-41: surface the need-to-login as an event so layout owns navigation. The previous
                // in-handler NavigateTo could race with concurrent requests and trigger redirect loops.
                auth.NotifyNeedsLogin();
            }
            else
            {
                logger.LogWarning(
                    "Token refresh is temporarily unavailable after 401 from {Method} {Endpoint}; preserving session",
                    request.Method.Method, endpoint);
            }
        }

        return response;
    }

    private static string EndpointForLog(Uri? uri)
    {
        if (uri is null) return "unknown";
        var path = uri.IsAbsoluteUri ? uri.AbsolutePath : uri.OriginalString.Split('?', 2)[0];
        var sanitized = new string(path.Where(c => !char.IsControl(c)).Take(256).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "/" : sanitized;
    }

    private static bool IsLoginRequest(Uri? uri)
    {
        if (uri is null) return false;
        var path = uri.IsAbsoluteUri ? uri.AbsolutePath : uri.OriginalString.Split('?', 2)[0];
        return path.Trim('/').Equals("api/auth/login", StringComparison.OrdinalIgnoreCase);
    }

    // S-TECH-30: clone a (buffered) request so it can be replayed after a token refresh. The body
    // was buffered before the first send, so ReadAsByteArrayAsync re-reads it without consuming a stream.
    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage req)
    {
        var clone = new HttpRequestMessage(req.Method, req.RequestUri) { Version = req.Version };
        foreach (var header in req.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        if (req.Content is not null)
        {
            var bytes = await req.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in req.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        return clone;
    }

    /// <summary>
    /// Single-flight renewal: concurrent callers all await the same renewal Task and
    /// therefore all benefit from the fresh token before their request is signed.
    /// A server-side 401 forces renewal even when the JWT expiry still looks valid locally.
    /// </summary>
    private async Task<RenewalOutcome> EnsureRenewedAsync(
        HttpRequestMessage outerRequest,
        CancellationToken ct,
        bool force = false,
        string? rejectedToken = null)
    {
        Task<RenewalOutcome> renewal;
        await _renewalLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (force && rejectedToken is not null
                && !string.Equals(auth.Token, rejectedToken, StringComparison.Ordinal)
                && auth.HasValidToken)
                return RenewalOutcome.Succeeded;
            if (!force && !auth.ShouldRenew()) return RenewalOutcome.NotNeeded;
            // The shared single-flight renewal must NOT be bound to the first caller's
            // CancellationToken: if that initiator is cancelled (navigation, disposed
            // component) it would fault every co-waiter that wasn't itself cancelled.
            // The refresh is best-effort and shared, so run it under CancellationToken.None.
            _inFlightRenewal ??= RenewAsync(outerRequest, CancellationToken.None);
            renewal = _inFlightRenewal;
        }
        finally
        {
            _renewalLock.Release();
        }

        try
        {
            return await renewal.ConfigureAwait(false);
        }
        finally
        {
            await _renewalLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (ReferenceEquals(_inFlightRenewal, renewal))
                    _inFlightRenewal = null;
            }
            finally
            {
                _renewalLock.Release();
            }
        }
    }

    private async Task<RenewalOutcome> RenewAsync(HttpRequestMessage outerRequest, CancellationToken ct)
    {
        try
        {
            var baseAddress = outerRequest.RequestUri is { IsAbsoluteUri: true } absUri
                ? new Uri(absUri.GetLeftPart(UriPartial.Authority))
                : null;
            if (baseAddress is null) return RenewalOutcome.Unavailable;

            // F-012: prefer refresh token rotation over the old /api/auth/renew endpoint.
            if (!string.IsNullOrEmpty(auth.RefreshToken))
            {
                using var refresh = new HttpRequestMessage(HttpMethod.Post, new Uri(baseAddress, "api/auth/token/refresh"));
                refresh.Headers.Add(RenewalMarkerHeader, "1");
                refresh.Content = JsonContent.Create(
                    new RefreshTokenRequest { RefreshToken = auth.RefreshToken },
                    options: JsonOptions.Web);

                using var refreshResp = await base.SendAsync(refresh, ct).ConfigureAwait(false);
                if (refreshResp.IsSuccessStatusCode)
                {
                    var body = await refreshResp.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions.Web, ct).ConfigureAwait(false);
                    if (body is { Token.Length: > 0 })
                    {
                        await auth.LoginAsync(body.Token, body.RefreshToken);
                        return RenewalOutcome.Succeeded;
                    }
                }

                if (IsDefinitiveRejection(refreshResp.StatusCode))
                    return RenewalOutcome.Rejected;

                logger.LogWarning("Refresh-token endpoint returned transient HTTP {StatusCode}; preserving session",
                    (int)refreshResp.StatusCode);
                return RenewalOutcome.Unavailable;
            }

            // Fallback: legacy renew endpoint (for bootstrap admin tokens without refresh tokens)
            using var renew = new HttpRequestMessage(HttpMethod.Post, new Uri(baseAddress, "api/auth/renew"));
            renew.Headers.Add(RenewalMarkerHeader, "1");
            renew.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

            using var response = await base.SendAsync(renew, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var legacyBody = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions.Web, ct).ConfigureAwait(false);
                if (legacyBody is { Token.Length: > 0 })
                {
                    await auth.LoginAsync(legacyBody.Token, legacyBody.RefreshToken);
                    return RenewalOutcome.Succeeded;
                }
            }

            if (IsDefinitiveRejection(response.StatusCode))
                return RenewalOutcome.Rejected;

            logger.LogWarning("Legacy token-renewal endpoint returned transient HTTP {StatusCode}; preserving session",
                (int)response.StatusCode);
            return RenewalOutcome.Unavailable;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Token renewal request failed transiently; preserving session");
            return RenewalOutcome.Unavailable;
        }
    }

    private static bool IsDefinitiveRejection(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    private enum RenewalOutcome
    {
        NotNeeded,
        Succeeded,
        Rejected,
        Unavailable
    }
}
