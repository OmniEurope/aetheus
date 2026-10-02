// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Tests.TestDoubles;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Aetheus.Front.Tests.Shared;

/// <summary>
/// Behavioural tests for DialogCloseButton.razor.cs: clicking the button invokes
/// OmniDialogService.Close (captured by a spy), and the optional Text parameter overrides the
/// default "Close" label.
/// </summary>
public class DialogCloseButtonTests : BunitContext
{
    public DialogCloseButtonTests()
    {
        BunitTestHelper.RegisterServices(this);
        Services.AddSingleton<OmniDialogService>(sp => new SpyDialogService(
            sp.GetRequiredService<NavigationManager>(),
            sp.GetRequiredService<IJSRuntime>()));
    }

    private SpyDialogService Spy() => (SpyDialogService)Services.GetRequiredService<OmniDialogService>();

    [Fact]
    public void Click_InvokesDialogClose()
    {
        var cut = Render<DialogCloseButton>();

        Assert.False(Spy().Closed);
        cut.Find("button").Click();

        Assert.True(Spy().Closed);
    }

    [Fact]
    public void DefaultLabel_IsClose()
    {
        var cut = Render<DialogCloseButton>();

        // No Text parameter ⇒ localized "Close" key (stub returns it verbatim).
        Assert.Contains("Close", cut.Markup);
    }

    [Fact]
    public void CustomText_OverridesLabel()
    {
        var cut = Render<DialogCloseButton>(p => p.Add(c => c.Text, "Dismiss"));

        Assert.Contains("Dismiss", cut.Markup);
    }
}
