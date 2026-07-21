// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using NSubstitute;

namespace Aetheus.Front.Tests;

/// <summary>
/// Extended AuthDelegatingHandler tests covering the renewal flow
/// (EnsureRenewedAsync / RenewAsync) - 65 uncovered lines.
/// </summary>
public class AuthDelegatingHandlerExtendedTests
{
    private static string CreateJwt(DateTimeOffset exp)
    {
        var hdr = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\"}"));
        var pay = JsonSerializer.SerializeToUtf8Bytes(new { sub = "u", exp = exp.ToUnixTimeSeconds() });
        var p64 = Convert.ToBase64String(pay).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{hdr}.{p64}.s";
    }

    private static (AuthStateProvider Auth, AuthDelegatingHandler Handler, HttpClient Client) CreateSetup(
        string? token, string? refreshToken, HttpStatusCode innerStatus = HttpStatusCode.OK,
        LoginResponse? renewResponse = null, LoginResponse? refreshResponse = null)
    {
        var auth = new AuthStateProvider(Substitute.For<IJSRuntime>(), NullLogger<AuthStateProvider>.Instance);
        if (token is not null)
        {
            typeof(AuthStateProvider).GetProperty("Token")!.SetValue(auth, token);
            if (refreshToken is not null)
                typeof(AuthStateProvider).GetProperty("RefreshToken")!.SetValue(auth, refreshToken);
        }

        var inner = new TestInnerHandler(innerStatus, renewResponse, refreshResponse);
        var handler = new AuthDelegatingHandler(auth, NullLogger<AuthDelegatingHandler>.Instance)
        {
            InnerHandler = inner
        };
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        return (auth, handler, client);
    }

    // === Token renewal via refresh token ===

    [Fact]
    public async Task SendAsync_ShouldRenew_WithRefreshToken_RenewsViaRefreshEndpoint()
    {
        var expiringToken = CreateJwt(DateTimeOffset.UtcNow.AddMinutes(2));
        var freshToken = CreateJwt(DateTimeOffset.UtcNow.AddHours(1));
        var refreshResponse = new LoginResponse { Token = freshToken, RefreshToken = "new-refresh" };

        var (auth, handler, client) = CreateSetup(expiringToken, "old-refresh",
            refreshResponse: refreshResponse);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/data");
        await client.SendAsync(request, Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(freshToken, auth.Token);
        Assert.Equal("new-refresh", auth.RefreshToken);
        client.Dispose();
        handler.Dispose();
    }

    // === Token renewal via legacy /renew ===

    [Fact]
    public async Task SendAsync_ShouldRenew_NoRefreshToken_RenewsViaLegacyEndpoint()
    {
        var expiringToken = CreateJwt(DateTimeOffset.UtcNow.AddMinutes(2));
        var freshToken = CreateJwt(DateTimeOffset.UtcNow.AddHours(1));
        var renewResponse = new LoginResponse { Token = freshToken };

        var (auth, handler, client) = CreateSetup(expiringToken, null,
            renewResponse: renewResponse);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/data");
        await client.SendAsync(request, Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(freshToken, auth.Token);
        client.Dispose();
        handler.Dispose();
    }

    // === Renewal failure - falls through gracefully ===

    [Fact]
    public async Task SendAsync_RenewalFails_FallsThroughToOriginalRequest()
    {
        var expiringToken = CreateJwt(DateTimeOffset.UtcNow.AddMinutes(2));

        var (auth, handler, client) = CreateSetup(expiringToken, null,
            innerStatus: HttpStatusCode.OK);
        // No renewResponse configured → renewal returns 200 but no body → falls through

        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/data");
        var response = await client.SendAsync(request, Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        client.Dispose();
        handler.Dispose();
    }

    // === 401 on API endpoint triggers NotifyNeedsLogin ===

    [Fact]
    public async Task SendAsync_401_NotifiesNeedsLogin()
    {
        var token = CreateJwt(DateTimeOffset.UtcNow.AddHours(1));
        var (auth, handler, client) = CreateSetup(token, null, HttpStatusCode.Unauthorized);

        var loginFired = false;
        auth.OnNeedsLogin += () => loginFired = true;

        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/data");
        await client.SendAsync(request, Xunit.TestContext.Current.CancellationToken);

        Assert.True(loginFired);
        Assert.Null(auth.Token);
        client.Dispose();
        handler.Dispose();
    }

    // === Expired token + dead refresh token: failed refresh must logout, not replay forever ===

    [Fact]
    public async Task SendAsync_401_ExpiredToken_DeadRefreshToken_LogsOutAndNotifies()
    {
        // Regression: the token is already expired AND the refresh token is rejected by the backend
        // (inner returns 401 for /token/refresh and /renew because no *Response is configured). The
        // refresh attempt leaves the stale expired token in memory; the handler must still recognise
        // the failure and force logout + redirect rather than replaying the dead token and returning 401.
        var expiredToken = CreateJwt(DateTimeOffset.UtcNow.AddMinutes(-10));
        var (auth, handler, client) = CreateSetup(expiredToken, "dead-refresh", HttpStatusCode.Unauthorized);

        var loginFired = false;
        auth.OnNeedsLogin += () => loginFired = true;

        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/data");
        var response = await client.SendAsync(request, Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(loginFired);
        Assert.Null(auth.Token);
        Assert.Null(auth.RefreshToken);
        client.Dispose();
        handler.Dispose();
    }

    // === Non-expired but server-rejected token + dead refresh: must logout, not replay ===

    [Fact]
    public async Task SendAsync_401_NonExpiredButRejectedToken_DeadRefreshToken_LogsOutAndNotifies()
    {
        // Regression: the access token is still in its validity window (NOT expired) but the server
        // rejects it with 401 - e.g. its SecurityStamp was invalidated by a DB restore, the signing
        // key rotated, or the user was disabled. The refresh token is also stale (inner returns 401).
        // HasValidToken stays true (token not expired), so the success check must additionally require
        // that the refresh produced a *different* token; otherwise the handler replays the dead token
        // forever and never logs out.
        var liveButRejectedToken = CreateJwt(DateTimeOffset.UtcNow.AddHours(1));
        var (auth, handler, client) = CreateSetup(liveButRejectedToken, "dead-refresh", HttpStatusCode.Unauthorized);

        var loginFired = false;
        auth.OnNeedsLogin += () => loginFired = true;

        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/data");
        var response = await client.SendAsync(request, Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(loginFired);
        Assert.Null(auth.Token);
        Assert.Null(auth.RefreshToken);
        client.Dispose();
        handler.Dispose();
    }

    // === Concurrent renewal already produced a valid token: retry once, do NOT log out ===

    [Fact]
    public async Task SendAsync_401_ConcurrentlyRefreshedValidToken_RetriesAndSucceeds_NoLogout()
    {
        // F9 regression: a concurrent request already refreshed the token (single-flight) before this
        // request reached the 401 handler, so auth.Token is valid but *unchanged* from this request's
        // point of view. The previous "token must have changed" guard logged the user out despite the
        // fresh, valid token. The handler must instead retry once with the valid token and surface the
        // retry's success.
        var validToken = CreateJwt(DateTimeOffset.UtcNow.AddHours(1));
        var auth = new AuthStateProvider(Substitute.For<IJSRuntime>(), NullLogger<AuthStateProvider>.Instance);
        typeof(AuthStateProvider).GetProperty("Token")!.SetValue(auth, validToken);
        typeof(AuthStateProvider).GetProperty("RefreshToken")!.SetValue(auth, "live-refresh");

        var inner = new FlipOnRetryHandler();
        var handler = new AuthDelegatingHandler(auth, NullLogger<AuthDelegatingHandler>.Instance) { InnerHandler = inner };
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        var loginFired = false;
        auth.OnNeedsLogin += () => loginFired = true;

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/data"), Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(loginFired);
        Assert.Equal(validToken, auth.Token); // not logged out
        client.Dispose();
        handler.Dispose();
    }

    // === 401 on login endpoint does NOT trigger logout ===

    [Fact]
    public async Task SendAsync_401OnLogin_DoesNotLogout()
    {
        var token = CreateJwt(DateTimeOffset.UtcNow.AddHours(1));
        var (auth, handler, client) = CreateSetup(token, null, HttpStatusCode.Unauthorized);

        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/api/auth/login");
        await client.SendAsync(request, Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(token, auth.Token); // not cleared
        client.Dispose();
        handler.Dispose();
    }

    // === Inner handler that simulates renewal endpoints ===

    private sealed class TestInnerHandler(
        HttpStatusCode normalStatus,
        LoginResponse? renewResponse,
        LoginResponse? refreshResponse) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri?.PathAndQuery ?? "";

            if (url.Contains("api/auth/token/refresh") && refreshResponse is not null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(refreshResponse), Encoding.UTF8, "application/json")
                });
            }

            if (url.Contains("api/auth/renew") && renewResponse is not null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(renewResponse), Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(normalStatus)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }

    // Returns 401 for the first /api/data hit (stale token used at send time) and 200 for the retry
    // (the token is accepted the second time - it was refreshed concurrently). Other URLs return 200.
    private sealed class FlipOnRetryHandler : HttpMessageHandler
    {
        private int _dataCalls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri?.PathAndQuery ?? "";
            var status = url.Contains("api/data") && Interlocked.Increment(ref _dataCalls) == 1
                ? HttpStatusCode.Unauthorized
                : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }
}
