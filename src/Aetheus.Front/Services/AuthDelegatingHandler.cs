// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aetheus.Shared.DTOs;
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
    private Task? _inFlightRenewal;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var isRenewalRequest = request.Headers.Contains(RenewalMarkerHeader);

        if (!isRenewalRequest && auth.ShouldRenew())
            await EnsureRenewedAsync(request, cancellationToken).ConfigureAwait(false);

        var token = auth.Token;
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // S-TECH-30: buffer the body up-front so the request can be re-sent verbatim if the access
        // token turns out to have expired (the 401 path below clones and retries with a fresh token).
        if (!isRenewalRequest && request.Content is not null)
            await request.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized && !isRenewalRequest &&
            !request.RequestUri!.PathAndQuery.Contains("/api/auth/login", StringComparison.OrdinalIgnoreCase))
        {
            // Attempt a refresh before giving up - the access token may have expired
            // while the refresh token is still valid (e.g. after a full-page reload).
            if (!string.IsNullOrEmpty(auth.RefreshToken))
            {
                try
                {
                    await EnsureRenewedAsync(request, cancellationToken).ConfigureAwait(false);

                    // Retry exactly once whenever we now hold a valid token - whether THIS call refreshed
                    // it or a *concurrent* request already refreshed it (single-flight) before we got here.
                    // The earlier "token must have changed" guard regressed the latter case: a concurrent
                    // renewal left auth.Token unchanged from our perspective, so the request was logged out
                    // despite a perfectly fresh, valid token. Only the retry's own response decides logout,
                    // so a genuinely dead-but-not-expired token (SecurityStamp invalidated, signing key
                    // rotated, user disabled) still reaches the logout below on its second 401 - no loop.
                    if (auth.HasValidToken)
                    {
                        logger.LogDebug("Valid token available after 401 - retrying original request once");
                        var retry = await CloneRequestAsync(request).ConfigureAwait(false);
                        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
                        var retryResponse = await base.SendAsync(retry, cancellationToken).ConfigureAwait(false);
                        response.Dispose();
                        if (retryResponse.StatusCode != HttpStatusCode.Unauthorized)
                            return retryResponse;
                        // Still unauthorized - the token is genuinely rejected. Surface its status and
                        // fall through to logout.
                        response = retryResponse;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Token refresh attempt after 401 failed");
                }
            }

            await auth.LogoutAsync();
            // F-41: surface the need-to-login as an event so layout owns navigation. The previous
            // in-handler NavigateTo could race with concurrent requests and trigger redirect loops.
            auth.NotifyNeedsLogin();
        }

        return response;
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
    /// </summary>
    private async Task EnsureRenewedAsync(HttpRequestMessage outerRequest, CancellationToken ct)
    {
        Task renewal;
        await _renewalLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!auth.ShouldRenew()) return;
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
            await renewal.ConfigureAwait(false);
        }
        finally
        {
            await _renewalLock.WaitAsync(ct).ConfigureAwait(false);
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

    private async Task RenewAsync(HttpRequestMessage outerRequest, CancellationToken ct)
    {
        try
        {
            var baseAddress = outerRequest.RequestUri is { IsAbsoluteUri: true } absUri
                ? new Uri(absUri.GetLeftPart(UriPartial.Authority))
                : null;
            if (baseAddress is null) return;

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
                        return;
                    }
                }
            }

            // Fallback: legacy renew endpoint (for bootstrap admin tokens without refresh tokens)
            using var renew = new HttpRequestMessage(HttpMethod.Post, new Uri(baseAddress, "api/auth/renew"));
            renew.Headers.Add(RenewalMarkerHeader, "1");
            renew.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

            using var response = await base.SendAsync(renew, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return;

            var legacyBody = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions.Web, ct).ConfigureAwait(false);
            if (legacyBody is { Token.Length: > 0 })
                await auth.LoginAsync(legacyBody.Token, legacyBody.RefreshToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Renewal is best-effort - log and let the original request proceed.
            // If the token has truly expired the 401 handler above will redirect to /login.
            logger.LogDebug(ex, "Token renewal failed");
        }
    }
}
