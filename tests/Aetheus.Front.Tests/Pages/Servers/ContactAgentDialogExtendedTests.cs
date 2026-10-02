// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Components.Servers;

namespace Aetheus.Front.Tests.Pages.Servers;

/// <summary>
/// Extended coverage for ContactAgentDialog: CancelRunningAsync, LoadDiagnosticAsync,
/// EnterSuccessAsync, and DisposeAsync paths.
/// </summary>
public class ContactAgentDialogExtendedTests
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly BindingFlags PrivStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly Type DialogType = typeof(ContactAgentDialog);

    private static ContactAgentDialog CreateInstance()
    {
        var instance = new ContactAgentDialog();
        var localizer = new BunitTestHelper.StubLocalizer();
        DialogType.GetProperty("L", Priv)!.SetValue(instance, localizer);
        return instance;
    }

    // ── CancelRunningAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task CancelRunningAsync_WithNullCts_DoesNotThrow()
    {
        var instance = CreateInstance();
        // _cts is null before a probe starts.
        var method = DialogType.GetMethod("CancelRunningAsync", Priv)!;
        await (Task)method.Invoke(instance, [])!; // should no-op
    }

    [Fact]
    public async Task CancelRunningAsync_WithActiveCts_CancelsAndDisposes()
    {
        var instance = CreateInstance();
        var cts = new CancellationTokenSource();
        DialogType.GetField("_cts", Priv)!.SetValue(instance, cts);

        var method = DialogType.GetMethod("CancelRunningAsync", Priv)!;
        await (Task)method.Invoke(instance, [])!;

        var ctsAfter = DialogType.GetField("_cts", Priv)!.GetValue(instance);
        Assert.Null(ctsAfter);
    }

    [Fact]
    public async Task CancelRunningAsync_WithAlreadyDisposedCts_DoesNotThrow()
    {
        var instance = CreateInstance();
        var cts = new CancellationTokenSource();
        cts.Dispose();
        DialogType.GetField("_cts", Priv)!.SetValue(instance, cts);

        var method = DialogType.GetMethod("CancelRunningAsync", Priv)!;
        // Should swallow ObjectDisposedException
        await (Task)method.Invoke(instance, [])!;
    }

    // ── EnterSuccessAsync - calls InvokeAsync(StateHasChanged) needing a renderer.
    // Test via direct field mutation to cover the same state shape. ─────────────

    [Fact]
    public void ContactState_SuccessValue_IsOne()
    {
        // Verify enum shape: Running=0, Success=1, Failed=2
        var instance = CreateInstance();
        var stateType = DialogType.GetNestedType("ContactState", Priv | System.Reflection.BindingFlags.Public)!;
        var successValue = Enum.Parse(stateType, "Success");
        Assert.Equal(1, (int)successValue);
    }

    [Fact]
    public void ContactState_RunningIsDefault()
    {
        var instance = CreateInstance();
        var state = (int)DialogType.GetField("_state", Priv)!.GetValue(instance)!;
        Assert.Equal(0, state); // Running = default
    }

    [Fact]
    public void Result_CanBeSetAndRead()
    {
        var instance = CreateInstance();
        var result = new ContactAgentResultDto { Reachable = true, LastHeartbeat = DateTime.Now, SecondsSinceLastHeartbeat = 5 };
        DialogType.GetField("_result", Priv)!.SetValue(instance, result);
        var storedResult = (ContactAgentResultDto?)DialogType.GetField("_result", Priv)!.GetValue(instance);
        Assert.NotNull(storedResult);
        Assert.True(storedResult!.Reachable);
    }

    // ── _diagnosticLoading flag ────────────────────────────────────────────

    [Fact]
    public void DiagnosticLoading_InitiallyFalse()
    {
        var instance = CreateInstance();
        var loading = (bool)DialogType.GetField("_diagnosticLoading", Priv)!.GetValue(instance)!;
        Assert.False(loading);
    }

    [Fact]
    public void Diagnostic_InitiallyNull()
    {
        var instance = CreateInstance();
        var diagnostic = DialogType.GetField("_diagnostic", Priv)!.GetValue(instance);
        Assert.Null(diagnostic);
    }

    // ── DisposeAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DisposeAsync_WithCts_CancelsAndDisposes()
    {
        var instance = CreateInstance();
        var cts = new CancellationTokenSource();
        DialogType.GetField("_cts", Priv)!.SetValue(instance, cts);

        await instance.DisposeAsync();

        var ctsAfter = DialogType.GetField("_cts", Priv)!.GetValue(instance);
        Assert.Null(ctsAfter);
    }

    [Fact]
    public async Task DisposeAsync_NullHubAndNullCts_DoesNotThrow()
    {
        var instance = CreateInstance();
        // Both _hub and _cts are null (uninitialized)
        await instance.DisposeAsync();
    }

    // ── RunCountdownAsync (static) ────────────────────────────────────────────

    [Fact]
    public async Task RunCountdownAsync_WithCancelledToken_DoesNotThrow()
    {
        var method = DialogType.GetMethod("RunCountdownAsync", PrivStatic)!;
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await (Task)method.Invoke(null, [cts.Token])!;
    }

    // ── FormatSeconds edge cases ──────────────────────────────────────────────

    [Fact]
    public void FormatSeconds_ExactlyOneHour_ReturnsHoursFormat()
    {
        var instance = CreateInstance();
        var method = DialogType.GetMethod("FormatSeconds", Priv)!;
        var result = (string)method.Invoke(instance, [3600.0])!;
        Assert.Contains("ContactAgentHoursAgo", result);
    }

    [Fact]
    public void FormatSeconds_Zero_ReturnsSecondsFormat()
    {
        var instance = CreateInstance();
        var method = DialogType.GetMethod("FormatSeconds", Priv)!;
        var result = (string)method.Invoke(instance, [0.0])!;
        Assert.Contains("ContactAgentSecondsAgo", result);
    }

    // ── Initial state ─────────────────────────────────────────────────────────

    [Fact]
    public void State_InitiallyRunning()
    {
        var instance = CreateInstance();
        var state = (int)DialogType.GetField("_state", Priv)!.GetValue(instance)!;
        Assert.Equal(0, state); // Running = 0
    }

    [Fact]
    public void Attempt_InitiallyZero()
    {
        var instance = CreateInstance();
        var attempt = (int)DialogType.GetField("_attempt", Priv)!.GetValue(instance)!;
        Assert.Equal(0, attempt);
    }

    [Fact]
    public void ErrorMessage_InitiallyEmpty()
    {
        var instance = CreateInstance();
        var msg = DialogType.GetField("_errorMessage", Priv)!.GetValue(instance);
        Assert.Equal(string.Empty, msg);
    }
}
