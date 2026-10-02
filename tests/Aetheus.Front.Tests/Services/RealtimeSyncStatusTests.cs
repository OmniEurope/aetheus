// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Aetheus.Front.Tests.Services;

/// <summary>
/// The phone put down during a run: on return the page must say it is catching up while it is, and
/// only then. The status is driven here by the page's visibility and by real hub connections built by
/// <see cref="HubConnectionFactory"/>, whose reconnect events are raised the way SignalR raises them.
/// </summary>
public sealed class RealtimeSyncStatusTests : IDisposable
{
    private readonly FakeTimeProvider _time = new();
    private readonly RealtimeSyncStatus _status;
    private readonly HubConnectionFactory _factory;

    public RealtimeSyncStatusTests()
    {
        _status = new RealtimeSyncStatus(_time);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ApiBaseUrl"] = "http://127.0.0.1:1" })
            .Build();
        var auth = new AuthStateProvider(
            NSubstitute.Substitute.For<Microsoft.JSInterop.IJSRuntime>(), NullLogger<AuthStateProvider>.Instance);
        _factory = new HubConnectionFactory(config, auth, NullLogger<AuthDelegatingHandler>.Instance, _status);
    }

    public void Dispose() => _status.Dispose();

    [Fact]
    public async Task Resume_WhileAHubReconnects_ShowsUntilItHasReconnectedAndRefetched()
    {
        var hub = _factory.Create("servers");
        var refetch = new TaskCompletionSource();
        hub.RejoinOnReconnect(() => refetch.Task);

        _status.PageHidden();
        await HubEvents.RaiseAsync(hub, "Reconnecting", new IOException("socket closed while frozen"));
        _status.PageVisible();
        Assert.True(_status.IsSyncing);

        // The transport is back, but what was missed has not been fetched again yet.
        var reconnected = HubEvents.RaiseAsync(hub, "Reconnected", "connection-2");
        Assert.True(_status.IsSyncing);

        refetch.SetResult();
        await reconnected;
        Assert.False(_status.IsSyncing);
        Assert.Equal(0, _status.InFlight);
    }

    [Fact]
    public async Task Resume_WhenTheDropIsNoticedJustAfterWaking_ShowsThenHides()
    {
        var hub = _factory.Create("pipelines");
        hub.RejoinOnReconnect(() => Task.CompletedTask);

        _status.PageHidden();
        _time.Advance(TimeSpan.FromMinutes(3));
        _status.PageVisible();
        Assert.False(_status.IsSyncing);

        // The dead socket is detected a moment after the tab wakes.
        _time.Advance(TimeSpan.FromSeconds(1));
        await HubEvents.RaiseAsync(hub, "Reconnecting", new TimeoutException("server timeout"));
        Assert.True(_status.IsSyncing);

        await HubEvents.RaiseAsync(hub, "Reconnected", "connection-2");
        Assert.False(_status.IsSyncing);
    }

    [Fact]
    public void QuickResume_WithEverythingConnected_NeverShows()
    {
        var changes = 0;
        _status.Changed += () => changes++;

        _status.PageHidden();
        _time.Advance(TimeSpan.FromSeconds(2));
        _status.PageVisible();
        _time.Advance(RealtimeSyncStatus.SettleWindow);

        Assert.False(_status.IsSyncing);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void CatchUp_StartingAfterTheSettleWindow_IsNotTheReturns()
    {
        _status.PageHidden();
        _status.PageVisible();
        _time.Advance(RealtimeSyncStatus.SettleWindow);

        using var catchUp = _status.BeginCatchUp();

        Assert.False(_status.IsSyncing);
    }

    [Fact]
    public void CatchUp_ThatNeverEnds_IsHiddenAfterTheSafetyTimeout()
    {
        _status.PageHidden();
        var stuck = _status.BeginCatchUp();
        _status.PageVisible();
        Assert.True(_status.IsSyncing);

        _time.Advance(RealtimeSyncStatus.SafetyTimeout - TimeSpan.FromSeconds(1));
        Assert.True(_status.IsSyncing);
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.False(_status.IsSyncing);
        Assert.Equal(1, _status.InFlight);
        stuck.Dispose();
    }

    [Fact]
    public async Task ClosedHub_EndsItsCatchUp()
    {
        var hub = _factory.Create("servers");
        hub.RejoinOnReconnect(() => Task.CompletedTask);

        _status.PageHidden();
        await HubEvents.RaiseAsync(hub, "Reconnecting", new IOException("dropped"));
        _status.PageVisible();
        Assert.True(_status.IsSyncing);

        // SignalR gave up: nothing is catching up any more, the connection-lost overlay takes it from here.
        await HubEvents.RaiseAsync(hub, "Closed", new IOException("gave up"));
        Assert.False(_status.IsSyncing);
    }

    [Fact]
    public void Visible_WithoutAPriorHide_DoesNothing()
    {
        using var catchUp = _status.BeginCatchUp();

        _status.PageVisible();

        Assert.False(_status.IsSyncing);
    }

    [Fact]
    public void CatchUp_DisposedTwice_CountsOnce()
    {
        var first = _status.BeginCatchUp();
        using var second = _status.BeginCatchUp();

        first.Dispose();
        first.Dispose();

        Assert.Equal(1, _status.InFlight);
    }

    /// <summary>Raises a hub event through its backing delegate, as the SignalR client does.</summary>
    private static class HubEvents
    {
        public static Task RaiseAsync(HubConnection hub, string name, object? argument)
        {
            var field = typeof(HubConnection).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"HubConnection has no backing field for {name}.");
            var handler = (Delegate?)field.GetValue(hub)
                ?? throw new InvalidOperationException($"No handler is registered on {name}.");
            return Task.WhenAll(handler.GetInvocationList().Select(single => (Task)single.DynamicInvoke(argument)!));
        }
    }
}
