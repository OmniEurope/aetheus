// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Layout;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Aetheus.Front.Tests.Pages.Layout;

/// <summary>
/// Targets the reconnection orchestration in MainLayout.razor.cs - specifically the fix for a
/// backend that is down at first load: previously <c>_wasConnectedOnce</c> was only ever set from a
/// genuinely successful connect, so <c>HandleConnectionStateChanged</c>'s disconnect branch (gated on
/// <c>_wasConnectedOnce</c>) never surfaced the connection-lost dialog and the user got no feedback
/// and no recovery path short of a manual page reload. The fix adds <c>_connectAttempted</c> (set the
/// moment the boot bootstrap kicks off <c>TaskTracker.StartAsync</c>, regardless of outcome) and widens
/// the gate to <c>_wasConnectedOnce || _connectAttempted</c>.
///
/// Rendered UNAUTHENTICATED so <c>TaskTracker.StartAsync</c> never fires (see the
/// <c>Auth.IsAuthenticated</c> guard in <c>OnInitializedAsync</c>) - that keeps these tests
/// deterministic instead of racing the real (and comparatively slow) background hub-retry loop that
/// an authenticated render would spin up against the test harness's always-failing hub factory.
/// <c>ConnectionLostDialog</c> itself renders unconditionally in MainLayout.razor (outside the
/// authenticated branch), so the private orchestration fields/methods can still be driven directly via
/// reflection - the same technique <see cref="MainLayoutMethodCoverageTests"/> already uses for other
/// private MainLayout members.
/// </summary>
public class MainLayoutReconnexionTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Type LayoutType = typeof(MainLayout);
    private readonly BunitTestHelper.TestHandler _handler;
    private readonly FakeTimeProvider _time = new();

    public MainLayoutReconnexionTests()
    {
        _handler = BunitTestHelper.RegisterServices(this, authenticated: false);
        Services.AddSingleton<TimeProvider>(_time);
        _handler.SetResponse(HttpMethod.Get, "health/live", System.Net.HttpStatusCode.ServiceUnavailable);
    }

    private static void SetField(object instance, string name, object? value) =>
        LayoutType.GetField(name, Priv)!.SetValue(instance, value);

    private static T GetField<T>(object instance, string name) =>
        (T)LayoutType.GetField(name, Priv)!.GetValue(instance)!;

    private static Task InvokeHandleConnectionStateChanged(IRenderedComponent<MainLayout> cut, bool connected)
    {
        var method = LayoutType.GetMethod("HandleConnectionStateChanged", Priv)!;
        return cut.InvokeAsync(() =>
        {
            method.Invoke(cut.Instance, [connected]);
            return Task.CompletedTask;
        });
    }

    // ── (a) drop -> grace -> dialog shown -> reconnect -> dialog closes ───────

    [Fact]
    public async Task Drop_AfterPriorConnect_ShowsDialogAfterGrace_ThenReconnectCloses()
    {
        var cut = Render<MainLayout>();

        // Simulate the classic path: a genuinely successful connect sets _wasConnectedOnce.
        await InvokeHandleConnectionStateChanged(cut, true);
        Assert.True(GetField<bool>(cut.Instance, "_wasConnectedOnce"));
        Assert.False(GetField<bool>(cut.Instance, "_showOfflineDialog"));

        // Drop: must NOT show immediately - the 2s grace period guards against a reload/blip flash.
        await InvokeHandleConnectionStateChanged(cut, false);
        Assert.False(GetField<bool>(cut.Instance, "_showOfflineDialog"));
        _time.Advance(TimeSpan.FromSeconds(2));

        // After the grace period elapses, the dialog appears. Wait on the MARKUP (not just the field) so
        // the child ConnectionLostDialog has actually re-rendered with Visible=true before asserting.
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".omni-connection-overlay")), TimeSpan.FromSeconds(5));
        Assert.True(GetField<bool>(cut.Instance, "_showOfflineDialog"));

        // Reconnect: the dialog closes again, without a page reload.
        await InvokeHandleConnectionStateChanged(cut, true);
        Assert.False(GetField<bool>(cut.Instance, "_showOfflineDialog"));
        Assert.Empty(cut.FindAll(".omni-connection-overlay"));
    }

    // ── (b) drop-at-startup (never connected) still surfaces the dialog ───────

    [Fact]
    public async Task Drop_WithoutEverConnecting_StillArmsDialog_WhenConnectWasAttempted()
    {
        var cut = Render<MainLayout>();

        // Simulate the boot bootstrap having kicked off a connect attempt that never reached
        // Connected (backend down at first load) - _wasConnectedOnce stays false in this scenario,
        // which is exactly the case that used to permanently gate the dialog off.
        SetField(cut.Instance, "_connectAttempted", true);
        Assert.False(GetField<bool>(cut.Instance, "_wasConnectedOnce"));

        await InvokeHandleConnectionStateChanged(cut, false);
        Assert.False(GetField<bool>(cut.Instance, "_showOfflineDialog")); // still within the grace period
        _time.Advance(TimeSpan.FromSeconds(2));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".omni-connection-overlay")), TimeSpan.FromSeconds(5));
        Assert.True(GetField<bool>(cut.Instance, "_showOfflineDialog"));
    }

    [Fact]
    public async Task Drop_WithoutEverConnectingOrAttempting_DoesNotShowDialog()
    {
        // Sanity check for the opposite branch: a session that never attempted a connect at all
        // (e.g. never authenticated) must not show a spurious connection-lost dialog.
        var cut = Render<MainLayout>();

        Assert.False(GetField<bool>(cut.Instance, "_wasConnectedOnce"));
        Assert.False(GetField<bool>(cut.Instance, "_connectAttempted"));

        await InvokeHandleConnectionStateChanged(cut, false);
        cut.WaitForAssertion(() =>
        {
            Assert.False(GetField<bool>(cut.Instance, "_showOfflineDialog"));
            Assert.Empty(cut.FindAll(".omni-connection-overlay"));
        }, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task SignalRDrop_WithHealthyHttpBackend_DoesNotDeclareBackendOffline()
    {
        _handler.SetResponse(HttpMethod.Get, "health/live", System.Net.HttpStatusCode.OK);
        var cut = Render<MainLayout>();
        SetField(cut.Instance, "_connectAttempted", true);

        await InvokeHandleConnectionStateChanged(cut, false);
        _time.Advance(TimeSpan.FromSeconds(2));

        cut.WaitForAssertion(() =>
        {
            Assert.True(cut.Instance.BackendConnected);
            Assert.False(GetField<bool>(cut.Instance, "_showOfflineDialog"));
            Assert.Empty(cut.FindAll(".omni-connection-overlay"));
        }, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Reconnect_DuringTheLivenessProbe_DoesNotOpenTheOverlayAfterwards()
    {
        // Candidate 2444: the hub came back while the grace period's health/live probe was in
        // flight. Reconnecting cancels that probe, a cancelled probe answers "not live", and the
        // overlay opened on a connected page and stayed there.
        var probeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _handler.SetAsyncJsonResponse<object>(HttpMethod.Get, "health/live", async ct =>
        {
            probeStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new object();
        });
        var cut = Render<MainLayout>();
        SetField(cut.Instance, "_connectAttempted", true);

        await InvokeHandleConnectionStateChanged(cut, false);
        _time.Advance(TimeSpan.FromSeconds(2));
        await probeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken);

        await InvokeHandleConnectionStateChanged(cut, true);

        cut.WaitForAssertion(() => Assert.Null(GetField<CancellationTokenSource?>(cut.Instance, "_offlineDelayCts")),
            TimeSpan.FromSeconds(5));
        await Task.Delay(200, Xunit.TestContext.Current.CancellationToken);
        Assert.False(GetField<bool>(cut.Instance, "_showOfflineDialog"));
        Assert.Empty(cut.FindAll(".omni-connection-overlay"));
    }

    [Fact]
    public async Task Logout_CancelsPendingOfflineDialog_AndItStaysHidden()
    {
        var cut = Render<MainLayout>();
        SetField(cut.Instance, "_connectAttempted", true);

        await InvokeHandleConnectionStateChanged(cut, false);
        Assert.NotNull(GetField<CancellationTokenSource>(cut.Instance, "_offlineDelayCts"));

        await cut.InvokeAsync(() => cut.Instance.OnLogout());
        _time.Advance(TimeSpan.FromSeconds(2));

        Assert.False(GetField<bool>(cut.Instance, "_showOfflineDialog"));
        Assert.False(GetField<bool>(cut.Instance, "_connectAttempted"));
        Assert.Null(GetField<CancellationTokenSource?>(cut.Instance, "_offlineDelayCts"));
        Assert.Empty(cut.FindAll(".omni-connection-overlay"));
    }

    // ── OnManualReconnect with an expired session → redirect to /login ────

    [Fact]
    public async Task OnManualReconnect_SessionExpired_RedirectsToLogin()
    {
        var cut = Render<MainLayout>();
        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();

        var method = LayoutType.GetMethod("OnManualReconnect", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        Assert.Contains("login", nav.Uri);
    }
}
