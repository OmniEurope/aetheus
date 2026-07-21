// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Microsoft.Extensions.Localization;
using NSubstitute;
using Radzen;

namespace Aetheus.Front.Tests.Services;

/// <summary>
/// Tests for the ApiStatus overload of UiActions.RunAsync - the third variant
/// (lines 62–80 in UiActions.cs) that was previously at 0 % coverage.
/// </summary>
public class UiActionsApiStatusTests
{
    private readonly NotificationService _notification = new();
    private readonly NotifyHelper _toast;
    private readonly UiActions _sut;

    public UiActionsApiStatusTests()
    {
        var localizer = Substitute.For<IStringLocalizer<AppStrings>>();
        localizer[Arg.Any<string>()]
            .Returns(ci => new LocalizedString((string)ci[0], (string)ci[0]));
        _toast = new NotifyHelper(_notification, localizer);
        _sut = new UiActions(_toast);
    }

    // ── Success path ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ApiStatus_Success_ReturnsTrue()
    {
        var result = await _sut.RunAsync(
            () => Task.FromResult(new ApiStatus(true, false, false)),
            "Saved");

        Assert.True(result);
        var message = Assert.Single(_notification.Messages);
        Assert.Equal(NotificationSeverity.Success, message.Severity);
        Assert.Equal("Saved", message.Summary);
    }

    [Fact]
    public async Task RunAsync_ApiStatus_Success_InvokesOnSuccess()
    {
        var called = false;
        await _sut.RunAsync(
            () => Task.FromResult(new ApiStatus(true, false, false)),
            "Saved",
            onSuccess: () => { called = true; return Task.CompletedTask; });

        Assert.True(called);
    }

    [Fact]
    public async Task RunAsync_ApiStatus_Success_WithNullOnSuccess_DoesNotThrow()
    {
        var result = await _sut.RunAsync(
            () => Task.FromResult(new ApiStatus(true, false, false)),
            "Saved",
            onSuccess: null);

        Assert.True(result);
    }

    // ── Failure path ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ApiStatus_Failure_ReturnsFalse()
    {
        var result = await _sut.RunAsync(
            () => Task.FromResult(new ApiStatus(false, false, false)),
            "Deleted");

        Assert.False(result);
        var message = Assert.Single(_notification.Messages);
        Assert.Equal(NotificationSeverity.Error, message.Severity);
        Assert.Equal("SaveFailed", message.Detail);
    }

    [Fact]
    public async Task RunAsync_ApiStatus_Failure_DoesNotInvokeOnSuccess()
    {
        var called = false;
        await _sut.RunAsync(
            () => Task.FromResult(new ApiStatus(false, false, false)),
            "Deleted",
            onSuccess: () => { called = true; return Task.CompletedTask; });

        Assert.False(called);
    }

    [Fact]
    public async Task RunAsync_ApiStatus_NotFound_ReturnsFalse()
    {
        var result = await _sut.RunAsync(
            () => Task.FromResult(new ApiStatus(false, true, false)),
            "Deleted");

        Assert.False(result);
        Assert.Equal(NotificationSeverity.Error, Assert.Single(_notification.Messages).Severity);
    }

    [Fact]
    public async Task RunAsync_ApiStatus_Forbidden_ReturnsFalse()
    {
        var result = await _sut.RunAsync(
            () => Task.FromResult(new ApiStatus(false, false, true)),
            "Saved");

        Assert.False(result);
        Assert.Equal(NotificationSeverity.Error, Assert.Single(_notification.Messages).Severity);
    }

    // ── Custom keys ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ApiStatus_CustomKeys_ReturnsTrue()
    {
        var result = await _sut.RunAsync(
            () => Task.FromResult(new ApiStatus(true, false, false)),
            successKey: "CustomSuccess",
            errorKey: "CustomError",
            errorTitleKey: "CustomErrTitle",
            successTitleKey: "CustomSuccTitle");

        Assert.True(result);
        var message = Assert.Single(_notification.Messages);
        Assert.Equal("CustomSuccTitle", message.Summary);
        Assert.Equal("CustomSuccess", message.Detail);
    }
}
