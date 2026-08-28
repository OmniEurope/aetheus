// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Services;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aetheus.Front.Tests.Services;

public class AlertNotificationServiceTests
{
    [Fact]
    public void UnreadCount_IsZero_Initially()
    {
        var (svc, _, _) = CreateService();
        Assert.Equal(0, svc.UnreadCount);
    }

    [Fact]
    public void ClearUnread_SetsCountToZero()
    {
        var (svc, _, _) = CreateService();
        svc.ClearUnread();
        Assert.Equal(0, svc.UnreadCount);
    }

    [Fact]
    public void ClearUnread_InvokesOnChange()
    {
        var (svc, _, _) = CreateService();
        var invoked = false;
        svc.OnChange += () => invoked = true;

        svc.ClearUnread();

        Assert.True(invoked);
    }

    [Fact]
    public async Task StartAsync_DoesNothing_WhenNotAuthenticated()
    {
        var (svc, _, factory) = CreateService(authenticated: false);

        await svc.StartAsync();

        Assert.Equal(0, svc.UnreadCount);
        Assert.Equal(0, factory.CreateCount);
    }

    [Fact]
    public async Task StartAsync_DoesNothing_WhenTokenEmpty()
    {
        var (svc, _, factory) = CreateService(authenticated: true, token: "");

        await svc.StartAsync();

        Assert.Equal(0, svc.UnreadCount);
        Assert.Equal(0, factory.CreateCount);
    }

    [Fact]
    public async Task DisposeAsync_WithoutStart_LeavesTheValueUnchanged()
    {
        var (svc, _, _) = CreateService();
        await svc.DisposeAsync();
        Assert.Equal(0, svc.UnreadCount);
    }

    [Fact]
    public async Task StartAsync_TwiceCalls_IsIdempotent()
    {
        var (svc, _, factory) = CreateService(authenticated: true, token: "jwt", isAdmin: true);

        await svc.StartAsync();
        await svc.StartAsync();

        Assert.Equal(1, factory.CreateCount);
        Assert.True(factory.SendCount > 0);
        Assert.Equal(0, svc.UnreadCount);
        await svc.DisposeAsync();
    }

    private static (AlertNotificationService svc, AuthStateProvider auth, CountingHubConnectionFactory factory)
        CreateService(bool authenticated = false, string token = "", bool isAdmin = false)
    {
        var auth = new AuthStateProvider(new FakeJsRuntime(), NullLogger<AuthStateProvider>.Instance);
        if (authenticated && !string.IsNullOrEmpty(token))
        {
            typeof(AuthStateProvider).GetProperty(nameof(AuthStateProvider.Token))!.SetValue(auth, token);
            typeof(AuthStateProvider).GetProperty(nameof(AuthStateProvider.Roles))!
                .SetValue(auth, isAdmin ? new List<string> { "Admin" } : new List<string>());
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ApiBaseUrl"] = "http://test" })
            .Build();
        var factory = new CountingHubConnectionFactory(config, auth);
        return (new AlertNotificationService(auth, factory), auth, factory);
    }
}

internal sealed class CountingHubConnectionFactory(IConfiguration config, AuthStateProvider auth)
    : HubConnectionFactory(config, auth, NullLogger<AuthDelegatingHandler>.Instance)
{
    public int CreateCount { get; private set; }
    public int SendCount { get; private set; }
    public int StopAllCount { get; private set; }

    public override async Task StopAllAsync()
    {
        StopAllCount++;
        await base.StopAllAsync();
    }

    public override HubConnection Create(string hubPath, IRetryPolicy? retryPolicy = null)
    {
        CreateCount++;
        return new HubConnectionBuilder()
            .WithUrl($"http://test/hubs/{hubPath}", options =>
            {
                options.HttpMessageHandlerFactory = _ => new CountingFailHandler(this);
            })
            .Build();
    }

    private sealed class CountingFailHandler(CountingHubConnectionFactory owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            owner.SendCount++;
            return Task.FromException<HttpResponseMessage>(
                new HttpRequestException("Expected test transport failure"));
        }
    }
}

internal sealed class FakeJsRuntime : Microsoft.JSInterop.IJSRuntime
{
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        => ValueTask.FromResult(default(TValue)!);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        => ValueTask.FromResult(default(TValue)!);
}
