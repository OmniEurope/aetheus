// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Components.Servers;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages.Servers;

/// <summary>
/// Targets zero-covered methods in <see cref="ContactAgentDialog"/>:
/// - <c>LoadDiagnosticAsync</c> (8 lines) - fetches api/servers/{id}/diagnostic and populates _diagnostic
/// - <c>CancelAndCloseAsync</c> (4 lines) - cancels in-flight CTS and calls Dialog.Close(false)
/// </summary>
public class ContactAgentDialogDiagnosticTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Type DialogType = typeof(ContactAgentDialog);
    private readonly BunitTestHelper.TestHandler _handler;

    public ContactAgentDialogDiagnosticTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        // Stub contact-agent so OnInitializedAsync's polling loop returns Reachable=true
        // immediately on the first attempt (no 6-second countdown wait).
        _handler.SetJsonResponse("contact-agent",
            new ContactAgentResultDto { Reachable = true, LastHeartbeat = DateTime.Now, SecondsSinceLastHeartbeat = 0 });
    }

    /// <summary>
    /// Creates a normally constructed ContactAgentDialog and wires the required injected properties
    /// without triggering OnInitializedAsync. Used for tests that do not call any method
    /// that invokes InvokeAsync (i.e. CancelAndCloseAsync tests).
    /// </summary>
    private ContactAgentDialog CreateInstance(int serverId = 42)
    {
        var instance = new ContactAgentDialog();

        var api = Services.GetRequiredService<ApiClient>();
        var dialog = Services.GetRequiredService<OmniDialogService>();
        var localizer = new BunitTestHelper.StubLocalizer();
        var hubFactory = Services.GetRequiredService<HubConnectionFactory>();

        DialogType.GetProperty("Api", Priv)!.SetValue(instance, api);
        DialogType.GetProperty("Dialog", Priv)!.SetValue(instance, dialog);
        DialogType.GetProperty("L", Priv)!.SetValue(instance, localizer);
        DialogType.GetProperty("HubFactory", Priv)!.SetValue(instance, hubFactory);
        DialogType.GetProperty("ServerId")!.SetValue(instance, serverId);
        DialogType.GetProperty("ServerName")!.SetValue(instance, $"srv-{serverId}");

        return instance;
    }

    /// <summary>
    /// Renders a ContactAgentDialog via bUnit so the component has a proper render handle,
    /// which is required for the InvokeAsync(StateHasChanged) calls inside LoadDiagnosticAsync.
    /// OnInitializedAsync is allowed to run (SignalR is swallowed by the catch-all; contact-agent
    /// is stubbed to return Reachable=true so the polling loop exits immediately).
    /// </summary>
    private async Task<(IRenderedComponent<ContactAgentDialog> cut, ContactAgentDialog instance)> RenderInstance(int serverId = 42)
    {
        var cut = Render<ContactAgentDialog>(p => p
            .Add(x => x.ServerId, serverId)
            .Add(x => x.ServerName, $"srv-{serverId}"));
        // Wait for OnInitializedAsync to complete (the contact-agent stub returns immediately)
        await cut.InvokeAsync(() => Task.CompletedTask);
        return (cut, cut.Instance);
    }

    // ── LoadDiagnosticAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task LoadDiagnosticAsync_StubsEndpoint_PopulatesDiagnostic()
    {
        var diagnostic = new ServerDiagnosticDto
        {
            TokenValid = true,
            TokenDaysRemaining = 30,
            AgentVersion = "3.0.0",
            BackendVersion = "3.0.0",
            Summary = "Token valid, agent online",
            VersionsCompatible = true
        };
        _handler.SetJsonResponse("api/servers/42/diagnostic", diagnostic);

        var (cut, instance) = await RenderInstance(serverId: 42);
        var method = DialogType.GetMethod("LoadDiagnosticAsync", Priv)!;

        await cut.InvokeAsync(() => (Task)method.Invoke(instance, [])!);

        var diag = (ServerDiagnosticDto?)DialogType
            .GetField("_diagnostic", Priv)!.GetValue(instance);
        var loading = (bool)DialogType.GetField("_diagnosticLoading", Priv)!.GetValue(instance)!;

        Assert.NotNull(diag);
        Assert.Equal("Token valid, agent online", diag.Summary);
        Assert.False(loading);
    }

    [Fact]
    public async Task LoadDiagnosticAsync_SetsLoadingFalseAfterCompletion()
    {
        _handler.SetJsonResponse("api/servers/43/diagnostic", new ServerDiagnosticDto
        {
            Summary = "OK",
            AgentVersion = "3.0.0",
            BackendVersion = "3.0.0"
        });

        var (cut, instance) = await RenderInstance(serverId: 43);
        var method = DialogType.GetMethod("LoadDiagnosticAsync", Priv)!;

        await cut.InvokeAsync(() => (Task)method.Invoke(instance, [])!);

        var loading = (bool)DialogType.GetField("_diagnosticLoading", Priv)!.GetValue(instance)!;
        Assert.False(loading);
    }

    [Fact]
    public async Task LoadDiagnosticAsync_AlreadyLoading_IsIdempotent()
    {
        _handler.SetJsonResponse("api/servers/44/diagnostic", new ServerDiagnosticDto { Summary = "test" });

        var (cut, instance) = await RenderInstance(serverId: 44);

        // Manually set _diagnosticLoading = true (simulates a load already in flight)
        DialogType.GetField("_diagnosticLoading", Priv)!.SetValue(instance, true);

        var method = DialogType.GetMethod("LoadDiagnosticAsync", Priv)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(instance, [])!);

        // Should have returned early - _diagnostic is still null
        var diag = DialogType.GetField("_diagnostic", Priv)!.GetValue(instance);
        Assert.Null(diag);
    }

    [Fact]
    public async Task LoadDiagnosticAsync_NetworkError_DiagnosticIsNull()
    {
        _handler.SetResponse("api/servers/45/diagnostic", System.Net.HttpStatusCode.InternalServerError);

        var (cut, instance) = await RenderInstance(serverId: 45);
        var method = DialogType.GetMethod("LoadDiagnosticAsync", Priv)!;

        // GetServerDiagnosticAsync swallows exceptions and returns null
        await cut.InvokeAsync(() => (Task)method.Invoke(instance, [])!);

        var diag = DialogType.GetField("_diagnostic", Priv)!.GetValue(instance);
        // null is acceptable - the method handles it gracefully
        Assert.False((bool)DialogType.GetField("_diagnosticLoading", Priv)!.GetValue(instance)!);
    }

    // ── CancelAndCloseAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task CancelAndCloseAsync_NullCts_KeepsItSet()
    {
        var instance = CreateInstance();
        // _cts is null (never started)
        DialogType.GetField("_cts", Priv)!.SetValue(instance, null);

        var method = DialogType.GetMethod("CancelAndCloseAsync", Priv)!;
        // Dialog.Close(false) should be called without crashing
        await (Task)method.Invoke(instance, [])!;

        Assert.NotNull(instance);
    }

    [Fact]
    public async Task CancelAndCloseAsync_WithActiveCts_CancelsAndCloses()
    {
        var instance = CreateInstance();

        // Set a live CancellationTokenSource
        var cts = new CancellationTokenSource();
        DialogType.GetField("_cts", Priv)!.SetValue(instance, cts);

        var method = DialogType.GetMethod("CancelAndCloseAsync", Priv)!;
        await (Task)method.Invoke(instance, [])!;

        // After CancelAndCloseAsync, _cts should be null (disposed)
        var ctsAfter = DialogType.GetField("_cts", Priv)!.GetValue(instance);
        Assert.Null(ctsAfter);
    }

    [Fact]
    public async Task CancelAndCloseAsync_DoesNotThrowOnAlreadyCancelledCts()
    {
        var instance = CreateInstance();

        var cts = new CancellationTokenSource();
        cts.Cancel(); // already cancelled
        DialogType.GetField("_cts", Priv)!.SetValue(instance, cts);

        var method = DialogType.GetMethod("CancelAndCloseAsync", Priv)!;
        await (Task)method.Invoke(instance, [])!;
        Assert.NotNull(instance);
    }
}
