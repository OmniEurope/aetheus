// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Aetheus.Front.Components.Shared;

public sealed class AuthDelegatingHandler(
    AuthStateProvider auth,
    ILogger<AuthDelegatingHandler> logger) : DelegatingHandler
{
    // Marker header used to short-circuit re-entrant renewal calls so the renew
    // request itself doesn't trigger another renewal check.
    private const string RenewalMarkerHeader = "X-Aetheus-Renewal";

    private readonly SemaphoreSlim _renewalLock = new(1, 1);
    private Task<RenewalOutcome>? _inFlightRenewal;

    /// <summary>How long one shared renewal may take before it is abandoned as unavailable.</summary>
    internal TimeSpan RenewalTimeout { get; init; } = FrontendRuntimeDefaults.TokenRenewalTimeout;

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

        // A 401 on a request we never authenticated means "this endpoint needs a session", not "your
        // session died": there is no credential to renew, so asking the server to renew one produces
        // a second, guaranteed 401. An anonymous visitor hit that on every API call the page made -
        // fifteen console errors on /api/auth/renew in fourteen seconds, which is what failed the
        // deployment smoke probe of run 2325.
        var hasCredential = !string.IsNullOrEmpty(sentToken) || !string.IsNullOrEmpty(auth.RefreshToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized && !isRenewalRequest &&
            hasCredential && !IsLoginRequest(request.RequestUri))
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
                await EndRejectedSessionAsync(request.Method.Method, endpoint).ConfigureAwait(false);
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

    /// <summary>
    /// PLAN-005 lot 9 / D48: the session ends with its reason, which the layout says and reports.
    /// F-41: the need-to-login is an event so the layout owns navigation; an in-handler NavigateTo
    /// could race with concurrent requests and trigger redirect loops.
    /// </summary>
    private async Task EndRejectedSessionAsync(string method, string endpoint)
    {
        var reason = _lastRejectionReason ?? SessionEndReasons.RefreshRejected;
        logger.LogWarning(
            "Token refresh was definitively rejected ({Reason}) after 401 from {Method} {Endpoint}; clearing session",
            reason, method, endpoint);
        await auth.EndSessionAsync(reason);
        auth.NotifyNeedsLogin();
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
            // It is bounded by its own deadline instead (see RunRenewalAsync).
            _inFlightRenewal ??= RunRenewalAsync(outerRequest);
            renewal = _inFlightRenewal;
        }
        finally
        {
            _renewalLock.Release();
        }

        // Each caller still waits under its OWN token. Awaiting the shared task bare was what wedged a
        // tab left idle past the access token's life (2026-09-13): one renewal that never answered held
        // every later request - the SignalR negotiate, the liveness probe, the reconnect button - past
        // any timeout the caller had, and the connection-lost overlay stayed up with the backend online.
        return await renewal.WaitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the one shared renewal under <see cref="RenewalTimeout"/> and retires it when it ends,
    /// never earlier: a caller that stops waiting must not let the next one start a second renewal,
    /// which would send the same refresh token twice and read as a replay to the server.
    /// </summary>
    private async Task<RenewalOutcome> RunRenewalAsync(HttpRequestMessage outerRequest)
    {
        using var deadline = new CancellationTokenSource(RenewalTimeout);
        try
        {
            var outcome = await RenewAsync(outerRequest, deadline.Token).ConfigureAwait(false);
            if (outcome == RenewalOutcome.Unavailable && deadline.IsCancellationRequested)
                logger.LogWarning("Token renewal did not answer within {Timeout}; preserving session", RenewalTimeout);
            return outcome;
        }
        finally
        {
            // Taken after the caller that started this renewal has published it and released the
            // lock, so the renewal retired here is always the one in flight.
            await _renewalLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try { _inFlightRenewal = null; }
            finally { _renewalLock.Release(); }
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

            // PLAN-005 lot 9 / D50: another tab may have renewed already. Take its tokens first: a
            // fresh access token needs no renewal at all, and a rotated refresh token must be the one
            // sent, since sending the old one outside the grace window reads as a replay and revokes
            // every session of this user.
            var heldToken = auth.Token;
            await auth.ReloadFromStorageAsync().ConfigureAwait(false);
            if (!string.Equals(heldToken, auth.Token, StringComparison.Ordinal) && auth.HasValidToken && !auth.ShouldRenew())
                return RenewalOutcome.Succeeded;

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
                        // A grace-window answer carries no refresh token: the one this tab sent was
                        // already rotated, so pick up the rotated one another tab stored.
                        if (body.RefreshToken is null)
                            await auth.ReloadFromStorageAsync().ConfigureAwait(false);
                        return RenewalOutcome.Succeeded;
                    }
                }

                if (IsDefinitiveRejection(refreshResp.StatusCode))
                {
                    _lastRejectionReason = SessionEndReasons.ForRefreshRejection(await ReadCodeAsync(refreshResp, ct).ConfigureAwait(false));
                    return RenewalOutcome.Rejected;
                }

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
            {
                _lastRejectionReason = SessionEndReasons.RenewRejected;
                return RenewalOutcome.Rejected;
            }

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

    /// <summary>Why the last definitive rejection happened (a <c>SessionEndReasons</c> code).</summary>
    private string? _lastRejectionReason;

    /// <summary>The <c>Code</c> of a refusal's <c>ApiError</c> body; null when absent or unreadable.</summary>
    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return (await response.Content.ReadFromJsonAsync<ApiError>(JsonOptions.Web, ct).ConfigureAwait(false))?.Code;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException or HttpRequestException)
        {
            return null;
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
