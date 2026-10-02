// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using OmniEurope.Blazor.Components;

namespace Aetheus.Front.Tests.TestDoubles;

/// <summary>
/// Shared dialog spy used across the dialog component tests. Records that the dialog was closed and the
/// payload it was closed with (<see cref="Closed"/> / <see cref="LastResult"/>), then delegates to the
/// real <see cref="OmniDialogService.Close"/>. Delegating matters for tests that drive a page which
/// <c>await</c>s <c>Dialog.OpenAsync&lt;T&gt;()</c> and rely on <c>Close</c> to complete that task (the
/// Dashboards tests); for the direct-render dialog-component tests there is no open dialog, so the base
/// call is a harmless no-op and only the recording is observed.
/// </summary>
internal sealed class SpyDialogService : OmniDialogService
{
    private SpyDialogService(OmniOverlayService overlay) : base(new AppDialogs(overlay, new BunitTestHelper.StubLocalizer()), overlay) { }

    internal SpyDialogService(NavigationManager _, IJSRuntime __) : this(new OmniOverlayService()) { }

    public bool Closed { get; private set; }

    public object? LastResult { get; private set; }

    // Challenge (Moyen): asserting `Closed` alone is tautological (the test sets it via Close). Recording
    // that OpenAsync was actually invoked lets a test prove the SUT opened a dialog, not just that the
    // test itself closed one.
    public int OpenCount { get; private set; }

    public string? LastOpenedComponent { get; private set; }

    public override Task<object?> OpenAsync<T>(string title, Dictionary<string, object?>? parameters = null, OmniDialogOptions? options = null)
    {
        OpenCount++;
        LastOpenedComponent = typeof(T).Name;
        return base.OpenAsync<T>(title, parameters, options);
    }

    public override void Close(object? result = null)
    {
        Closed = true;
        LastResult = result;
        base.Close(result);
    }
}
