// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Layout;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace Aetheus.Front.Tests.Pages.Layout;

public class ConnectionLostDialogTests : BunitContext
{
    public ConnectionLostDialogTests() => BunitTestHelper.RegisterServices(this, authenticated: false);

    [Fact]
    public void NotVisible_RendersNothing()
    {
        var cut = Render<ConnectionLostDialog>(ps => ps.Add(p => p.Visible, false));

        Assert.DoesNotContain("connection-lost-mask", cut.Markup);
        Assert.DoesNotContain("cloud_off", cut.Markup);
    }

    [Fact]
    public void Visible_RendersOverlayTitleAndReconnectButton()
    {
        var cut = Render<ConnectionLostDialog>(ps => ps.Add(p => p.Visible, true));

        Assert.Contains("connection-lost-mask", cut.Markup);
        Assert.Contains("cloud_off", cut.Markup);
        // StubLocalizer echoes the resource key, so the presence of these keys proves the strings render.
        Assert.Contains("ConnectionLost", cut.Markup);
        Assert.Contains("ManualReconnect", cut.Markup);
    }

    [Fact]
    public void Countdown_RendersOnlyWhenPositive()
    {
        var atZero = Render<ConnectionLostDialog>(ps => ps
            .Add(p => p.Visible, true)
            .Add(p => p.ReconnectCountdown, 0));
        Assert.DoesNotContain("ReconnectIn", atZero.Markup);

        var counting = Render<ConnectionLostDialog>(ps => ps
            .Add(p => p.Visible, true)
            .Add(p => p.ReconnectCountdown, 5));
        Assert.Contains("ReconnectIn", counting.Markup);
    }

    [Fact]
    public void ManualReconnectButton_InvokesCallback()
    {
        var clicked = false;
        var cut = Render<ConnectionLostDialog>(ps => ps
            .Add(p => p.Visible, true)
            .Add(p => p.OnManualReconnect, EventCallback.Factory.Create(this, () => clicked = true)));

        cut.Find("button").Click();

        Assert.True(clicked);
    }
}
