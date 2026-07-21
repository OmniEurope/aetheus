// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Shared;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace Aetheus.Front.Tests.Shared;

public class ConfirmDialogTests : BunitContext
{
    public ConfirmDialogTests()
    {
        BunitTestHelper.RegisterServices(this);
    }

    [Fact]
    public void Invisible_RendersNothing()
    {
        var cut = Render<ConfirmDialog>(p => p.Add(x => x.Visible, false));
        Assert.DoesNotContain("dialog-overlay", cut.Markup);
    }

    [Fact]
    public void Visible_RendersTitleAndMessage()
    {
        var cut = Render<ConfirmDialog>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.Title, "Delete item")
            .Add(x => x.Message, "Are you sure?"));
        Assert.Contains("Delete item", cut.Markup);
        Assert.Contains("Are you sure?", cut.Markup);
    }

    [Fact]
    public void CustomConfirmText_IsRendered()
    {
        var cut = Render<ConfirmDialog>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.ConfirmText, "Revoke now"));
        Assert.Contains("Revoke now", cut.Markup);
    }

    [Fact]
    public async Task Click_Confirm_InvokesCallback()
    {
        var invoked = false;
        var cut = Render<ConfirmDialog>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.Title, "t")
            .Add(x => x.Message, "m")
            .Add(x => x.OnConfirm, EventCallback.Factory.Create(this, () => invoked = true)));

        var buttons = cut.FindAll("button");
        Assert.NotEmpty(buttons);
        await cut.InvokeAsync(() => buttons[^1].Click());
        Assert.True(invoked);
    }

    [Fact]
    public async Task Click_Cancel_InvokesCallback()
    {
        var invoked = false;
        var cut = Render<ConfirmDialog>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.Title, "t")
            .Add(x => x.Message, "m")
            .Add(x => x.OnCancel, EventCallback.Factory.Create(this, () => invoked = true)));

        // The cancel button is the first one with "Cancel" text
        var buttons = cut.FindAll("button");
        await cut.InvokeAsync(() => buttons[0].Click());
        Assert.True(invoked);
    }

    [Fact]
    public void IsBusy_RendersBusyOnConfirm()
    {
        var cut = Render<ConfirmDialog>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.IsBusy, true));
        var confirmButton = cut.FindAll("button")[^1];
        Assert.True(confirmButton.HasAttribute("disabled"));
        var busyIcon = Assert.Single(confirmButton.QuerySelectorAll("i"));
        Assert.Contains("animation", busyIcon.GetAttribute("style"));
    }
}
