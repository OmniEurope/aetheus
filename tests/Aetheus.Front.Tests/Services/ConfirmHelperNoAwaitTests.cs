// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Services;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Radzen;

namespace Aetheus.Front.Tests;

public class ConfirmHelperNoAwaitTests : BunitContext
{
    private sealed class RecordingDialogService(NavigationManager nav, IJSRuntime js) : DialogService(nav, js)
    {
        public string? Message { get; private set; }
        public string? Title { get; private set; }
        public ConfirmOptions? Options { get; private set; }

        public override Task<bool?> Confirm(string message, string title, ConfirmOptions? options = null, CancellationToken? cancellationToken = null)
        {
            Message = message;
            Title = title;
            Options = options;
            return Task.FromResult<bool?>(null);
        }
    }

    private (ConfirmHelper Helper, RecordingDialogService Dialog) CreateHelper()
    {
        BunitTestHelper.RegisterServices(this);
        var dialog = new RecordingDialogService(
            Services.GetRequiredService<NavigationManager>(),
            Services.GetRequiredService<IJSRuntime>());
        return (new ConfirmHelper(dialog, new BunitTestHelper.StubLocalizer()), dialog);
    }

    [Fact]
    public void ConfirmDeleteAsync_NoArgs_UsesDefaultTitleAndDeleteOptions()
    {
        var (helper, dialog) = CreateHelper();

        _ = helper.ConfirmDeleteAsync("DeleteServerConfirm");

        Assert.Equal("DeleteServerConfirm", dialog.Message);
        Assert.Equal("ConfirmDelete", dialog.Title);
        Assert.Equal("Delete", dialog.Options!.OkButtonText);
        Assert.Equal("Cancel", dialog.Options.CancelButtonText);
    }

    [Fact]
    public void ConfirmDeleteAsync_WithArgs_FormatsMessage()
    {
        var (helper, dialog) = CreateHelper();

        _ = helper.ConfirmDeleteAsync("Delete {0}?", "ConfirmDelete", "web-01");

        Assert.Equal("Delete web-01?", dialog.Message);
        Assert.Equal("ConfirmDelete", dialog.Title);
    }

    [Fact]
    public void ConfirmDeleteAsync_CustomTitleKey_UsesGivenTitle()
    {
        var (helper, dialog) = CreateHelper();

        _ = helper.ConfirmDeleteAsync("RemoveConfirm", "RemoveTitle");

        Assert.Equal("RemoveTitle", dialog.Title);
    }

    [Fact]
    public void ConfirmAsync_NoArgs_UsesDefaultTitleAndOkOptions()
    {
        var (helper, dialog) = CreateHelper();

        _ = helper.ConfirmAsync("AreYouSure");

        Assert.Equal("AreYouSure", dialog.Message);
        Assert.Equal("Confirm", dialog.Title);
        Assert.Equal("OK", dialog.Options!.OkButtonText);
        Assert.Equal("Cancel", dialog.Options.CancelButtonText);
    }

    [Fact]
    public void ConfirmAsync_WithArgs_FormatsMessage()
    {
        var (helper, dialog) = CreateHelper();

        _ = helper.ConfirmAsync("Promote {0}?", "Promote", "v1.2.3");

        Assert.Equal("Promote v1.2.3?", dialog.Message);
        Assert.Equal("Promote", dialog.Title);
    }

    [Fact]
    public void ConfirmAsync_CustomTitleKey_UsesGivenTitle()
    {
        var (helper, dialog) = CreateHelper();

        _ = helper.ConfirmAsync("ProceedConfirm", "ProceedTitle");

        Assert.Equal("ProceedTitle", dialog.Title);
    }
}
