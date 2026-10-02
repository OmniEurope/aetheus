// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using NSubstitute;

namespace Aetheus.Front.Tests;

/// <summary>
/// A token renewal that never answers used to wedge the whole browser session. A tab left alone for
/// longer than the 30-minute access token renews before its next request, and every request after
/// that - the SignalR negotiate, the liveness probe behind the connection-lost dialog, the reconnect
/// button - awaited that one shared renewal with no deadline, ignoring its own cancellation. The
/// overlay then stayed up with no countdown and no reason, backend online (2026-09-13).
/// </summary>
public class AuthDelegatingHandlerHungRenewalTests
{
    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task SendAsync_RenewalNeverAnswers_CallerCancellationStillEndsTheRequest()
    {
        var inner = new RefreshHangsHandler(honourCancellation: false);
        var (handler, client) = CreateExpiredSession(inner, renewalTimeout: TimeSpan.FromHours(1));
        using var callerTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var sending = client.GetAsync("http://localhost/hubs/servers/negotiate", callerTimeout.Token);

        // Before the fix this awaited the hung renewal forever: the guard turns that into a failure.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sending.WaitAsync(HangGuard, Xunit.TestContext.Current.CancellationToken));
        client.Dispose();
        handler.Dispose();
    }

    [Fact]
    public async Task SendAsync_RenewalNeverAnswers_IsAbandonedAtItsDeadlineAndTheRequestGoesOut()
    {
        var inner = new RefreshHangsHandler(honourCancellation: true);
        var (handler, client) = CreateExpiredSession(inner, renewalTimeout: TimeSpan.FromMilliseconds(200));

        var response = await client.GetAsync("http://localhost/health/live", Xunit.TestContext.Current.CancellationToken)
            .WaitAsync(HangGuard, Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, inner.RefreshCalls);
        client.Dispose();
        handler.Dispose();
    }

    [Fact]
    public async Task SendAsync_AfterAnAbandonedRenewal_TheNextRequestTriesAgain()
    {
        var inner = new RefreshHangsHandler(honourCancellation: true);
        var (handler, client) = CreateExpiredSession(inner, renewalTimeout: TimeSpan.FromMilliseconds(200));

        await client.GetAsync("http://localhost/health/live", Xunit.TestContext.Current.CancellationToken)
            .WaitAsync(HangGuard, Xunit.TestContext.Current.CancellationToken);
        await client.GetAsync("http://localhost/health/live", Xunit.TestContext.Current.CancellationToken)
            .WaitAsync(HangGuard, Xunit.TestContext.Current.CancellationToken);

        // A renewal that timed out must not stay parked as the in-flight one.
        Assert.Equal(2, inner.RefreshCalls);
        client.Dispose();
        handler.Dispose();
    }

    [Fact]
    public async Task SendAsync_OneCallerGivesUp_OthersKeepSharingTheSameRenewal()
    {
        // Leaving early must not clear the in-flight renewal: a second renewal would send the same
        // refresh token again, which the server reads as a replay and answers by revoking the sessions.
        var inner = new RefreshHangsHandler(honourCancellation: true);
        var (handler, client) = CreateExpiredSession(inner, renewalTimeout: TimeSpan.FromSeconds(2));
        using var impatient = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var first = client.GetAsync("http://localhost/api/a", impatient.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => first.WaitAsync(HangGuard, Xunit.TestContext.Current.CancellationToken));
        await client.GetAsync("http://localhost/api/b", Xunit.TestContext.Current.CancellationToken)
            .WaitAsync(HangGuard, Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(1, inner.RefreshCalls);
        client.Dispose();
        handler.Dispose();
    }

    private static (AuthDelegatingHandler Handler, HttpClient Client) CreateExpiredSession(
        HttpMessageHandler inner, TimeSpan renewalTimeout)
    {
        var auth = new AuthStateProvider(Substitute.For<IJSRuntime>(), NullLogger<AuthStateProvider>.Instance);
        typeof(AuthStateProvider).GetProperty("Token")!.SetValue(auth, CreateJwt(DateTimeOffset.UtcNow.AddMinutes(-40)));
        typeof(AuthStateProvider).GetProperty("RefreshToken")!.SetValue(auth, "live-refresh");
        var handler = new AuthDelegatingHandler(auth, NullLogger<AuthDelegatingHandler>.Instance)
        {
            InnerHandler = inner,
            RenewalTimeout = renewalTimeout
        };
        return (handler, new HttpClient(handler));
    }

    private static string CreateJwt(DateTimeOffset exp)
    {
        var hdr = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\"}"));
        var pay = JsonSerializer.SerializeToUtf8Bytes(new { sub = "u", exp = exp.ToUnixTimeSeconds() });
        var p64 = Convert.ToBase64String(pay).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{hdr}.{p64}.s";
    }

    /// <summary>
    /// The refresh endpoint never answers; everything else answers 200. A browser fetch honours an
    /// abort, so <paramref name="honourCancellation"/> is the realistic case; a request stuck on a dead
    /// connection before any abort is the other one.
    /// </summary>
    private sealed class RefreshHangsHandler(bool honourCancellation) : HttpMessageHandler
    {
        private int _refreshCalls;
        public int RefreshCalls => Volatile.Read(ref _refreshCalls);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/api/auth/token/refresh", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _refreshCalls);
                await Task.Delay(Timeout.Infinite, honourCancellation ? cancellationToken : CancellationToken.None);
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
