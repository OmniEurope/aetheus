// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Microsoft.Extensions.Logging;
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
        // The token is already expired and the refresh endpoint explicitly rejects the refresh token.
        // This is a definitive authentication failure, so the handler must clear the session.
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
        // Local JWT expiry is irrelevant once both the API and the independent refresh endpoint reject
        // the session. The explicit refresh rejection is the proof that permits logout.
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

    // === A server-side 401 forces refresh even when the JWT looks valid locally ===

    [Fact]
    public async Task SendAsync_401_NonExpiredToken_ForcesRefreshAndRetries()
    {
        var rejectedToken = CreateJwt(DateTimeOffset.UtcNow.AddHours(1));
        var freshToken = CreateJwt(DateTimeOffset.UtcNow.AddHours(2));
        var auth = new AuthStateProvider(Substitute.For<IJSRuntime>(), NullLogger<AuthStateProvider>.Instance);
        typeof(AuthStateProvider).GetProperty("Token")!.SetValue(auth, rejectedToken);
        typeof(AuthStateProvider).GetProperty("RefreshToken")!.SetValue(auth, "live-refresh");

        var inner = new DeploymentSwitchHandler(
            HttpStatusCode.OK,
            new LoginResponse { Token = freshToken, RefreshToken = "rotated-refresh" },
            HttpStatusCode.OK);
        var handler = new AuthDelegatingHandler(auth, NullLogger<AuthDelegatingHandler>.Instance) { InnerHandler = inner };
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        var loginFired = false;
        auth.OnNeedsLogin += () => loginFired = true;

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/data"), Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(loginFired);
        Assert.Equal(freshToken, auth.Token);
        Assert.Equal("rotated-refresh", auth.RefreshToken);
        Assert.Equal(1, inner.RefreshCalls);
        Assert.Equal([rejectedToken, freshToken], inner.DataAuthorizationTokens);
        client.Dispose();
        handler.Dispose();
    }

    [Fact]
    public async Task SendAsync_401_TransientRefreshFailure_PreservesSession()
    {
        var token = CreateJwt(DateTimeOffset.UtcNow.AddHours(1));
        var auth = new AuthStateProvider(Substitute.For<IJSRuntime>(), NullLogger<AuthStateProvider>.Instance);
        typeof(AuthStateProvider).GetProperty("Token")!.SetValue(auth, token);
        typeof(AuthStateProvider).GetProperty("RefreshToken")!.SetValue(auth, "live-refresh");

        var inner = new DeploymentSwitchHandler(HttpStatusCode.ServiceUnavailable, null, HttpStatusCode.OK);
        var handler = new AuthDelegatingHandler(auth, NullLogger<AuthDelegatingHandler>.Instance) { InnerHandler = inner };
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var loginFired = false;
        auth.OnNeedsLogin += () => loginFired = true;

        var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/data"),
            Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(loginFired);
        Assert.Equal(token, auth.Token);
        Assert.Equal("live-refresh", auth.RefreshToken);
        Assert.Equal(1, inner.RefreshCalls);
        client.Dispose();
        handler.Dispose();
    }

    [Fact]
    public async Task SendAsync_401_AfterSuccessfulRefresh_PreservesFreshSession()
    {
        var rejectedToken = CreateJwt(DateTimeOffset.UtcNow.AddHours(1));
        var freshToken = CreateJwt(DateTimeOffset.UtcNow.AddHours(2));
        var auth = new AuthStateProvider(Substitute.For<IJSRuntime>(), NullLogger<AuthStateProvider>.Instance);
        typeof(AuthStateProvider).GetProperty("Token")!.SetValue(auth, rejectedToken);
        typeof(AuthStateProvider).GetProperty("RefreshToken")!.SetValue(auth, "live-refresh");

        var inner = new DeploymentSwitchHandler(
            HttpStatusCode.OK,
            new LoginResponse { Token = freshToken, RefreshToken = "rotated-refresh" },
            HttpStatusCode.Unauthorized);
        var handler = new AuthDelegatingHandler(auth, NullLogger<AuthDelegatingHandler>.Instance) { InnerHandler = inner };
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var loginFired = false;
        auth.OnNeedsLogin += () => loginFired = true;

        var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/data"),
            Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(loginFired);
        Assert.Equal(freshToken, auth.Token);
        Assert.Equal("rotated-refresh", auth.RefreshToken);
        Assert.Equal(2, inner.DataCalls);
        client.Dispose();
        handler.Dispose();
    }

    [Fact]
    public async Task SendAsync_401_LogsEndpointWithoutQueryOrTokens()
    {
        var token = CreateJwt(DateTimeOffset.UtcNow.AddHours(1));
        var auth = new AuthStateProvider(Substitute.For<IJSRuntime>(), NullLogger<AuthStateProvider>.Instance);
        typeof(AuthStateProvider).GetProperty("Token")!.SetValue(auth, token);
        typeof(AuthStateProvider).GetProperty("RefreshToken")!.SetValue(auth, "live-refresh-secret");

        var logger = new RecordingLogger<AuthDelegatingHandler>();
        var inner = new DeploymentSwitchHandler(HttpStatusCode.ServiceUnavailable, null, HttpStatusCode.OK);
        var handler = new AuthDelegatingHandler(auth, logger) { InnerHandler = inner };
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get,
                "http://localhost/api/projects/42?access_token=secret-query-value"),
            Xunit.TestContext.Current.CancellationToken);

        var logs = string.Join('\n', logger.Messages);
        Assert.Contains("GET /api/projects/42 returned 401", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-query-value", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("live-refresh-secret", logs, StringComparison.Ordinal);
        Assert.DoesNotContain(token, logs, StringComparison.Ordinal);
        client.Dispose();
        handler.Dispose();
    }

    [Fact]
    public async Task SendAsync_401_LoginPathInQuery_StillForcesRefresh()
    {
        var rejectedToken = CreateJwt(DateTimeOffset.UtcNow.AddHours(1));
        var freshToken = CreateJwt(DateTimeOffset.UtcNow.AddHours(2));
        var auth = new AuthStateProvider(Substitute.For<IJSRuntime>(), NullLogger<AuthStateProvider>.Instance);
        typeof(AuthStateProvider).GetProperty("Token")!.SetValue(auth, rejectedToken);
        typeof(AuthStateProvider).GetProperty("RefreshToken")!.SetValue(auth, "live-refresh");

        var inner = new DeploymentSwitchHandler(
            HttpStatusCode.OK,
            new LoginResponse { Token = freshToken, RefreshToken = "rotated-refresh" },
            HttpStatusCode.OK);
        var handler = new AuthDelegatingHandler(auth, NullLogger<AuthDelegatingHandler>.Instance) { InnerHandler = inner };
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get,
                "http://localhost/api/projects?returnUrl=/api/auth/login"),
            Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, inner.RefreshCalls);
        Assert.Equal(freshToken, auth.Token);
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

    private sealed class DeploymentSwitchHandler(
        HttpStatusCode refreshStatus,
        LoginResponse? refreshResponse,
        HttpStatusCode retriedDataStatus) : HttpMessageHandler
    {
        public int DataCalls { get; private set; }
        public int RefreshCalls { get; private set; }
        public List<string?> DataAuthorizationTokens { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri?.PathAndQuery ?? "";
            if (url.Contains("api/auth/token/refresh", StringComparison.Ordinal))
            {
                RefreshCalls++;
                var content = refreshResponse is null ? "{}" : JsonSerializer.Serialize(refreshResponse);
                return Task.FromResult(new HttpResponseMessage(refreshStatus)
                {
                    Content = new StringContent(content, Encoding.UTF8, "application/json")
                });
            }

            DataCalls++;
            DataAuthorizationTokens.Add(request.Headers.Authorization?.Parameter);
            return Task.FromResult(new HttpResponseMessage(
                DataCalls == 1 ? HttpStatusCode.Unauthorized : retriedDataStatus)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
