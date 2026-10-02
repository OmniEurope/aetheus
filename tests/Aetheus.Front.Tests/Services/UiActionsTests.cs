// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using OmniEurope.Blazor.Components;

namespace Aetheus.Front.Tests.Services;

public class UiActionsTests
{
    private readonly OmniOverlayService _overlay = new(new FakeTimeProvider());
    private readonly NotifyHelper _toast;
    private readonly UiActions _sut;

    public UiActionsTests()
    {
        var localizer = Substitute.For<IStringLocalizer<AppStrings>>();
        localizer[Arg.Any<string>()]
            .Returns(ci => new LocalizedString((string)ci[0], (string)ci[0]));
        _toast = new NotifyHelper(_overlay, localizer);
        _sut = new UiActions(_toast);
    }

    // --- RunAsync<T> (class result) ---

    [Fact]
    public async Task RunAsyncT_Success_ReturnsTrue()
    {
        var result = await _sut.RunAsync(
            () => Task.FromResult<string?>("ok"),
            "Saved");

        Assert.True(result);
        AssertToast(OmniSeverity.Success, "Saved", "Saved");
    }

    [Fact]
    public async Task RunAsyncT_Success_InvokesOnSuccess()
    {
        var called = false;

        await _sut.RunAsync(
            () => Task.FromResult<string?>("ok"),
            "Saved",
            onSuccess: _ => { called = true; return Task.CompletedTask; });

        Assert.True(called);
        AssertToast(OmniSeverity.Success, "Saved", "Saved");
    }

    [Fact]
    public async Task RunAsyncT_Null_ReturnsFalse()
    {
        var result = await _sut.RunAsync(
            () => Task.FromResult<string?>(null),
            "Saved");

        Assert.False(result);
        AssertToast(OmniSeverity.Danger, "Error", "SaveFailed");
    }

    [Fact]
    public async Task RunAsyncT_Null_DoesNotInvokeOnSuccess()
    {
        var called = false;

        await _sut.RunAsync(
            () => Task.FromResult<string?>(null),
            "Saved",
            onSuccess: _ => { called = true; return Task.CompletedTask; });

        Assert.False(called);
        AssertToast(OmniSeverity.Danger, "Error", "SaveFailed");
    }

    // --- RunAsync (bool result) ---

    [Fact]
    public async Task RunAsyncBool_True_ReturnsTrue()
    {
        var result = await _sut.RunAsync(
            () => Task.FromResult(true),
            "Deleted");

        Assert.True(result);
        AssertToast(OmniSeverity.Success, "Saved", "Deleted");
    }

    [Fact]
    public async Task RunAsyncBool_True_InvokesOnSuccess()
    {
        var called = false;

        await _sut.RunAsync(
            () => Task.FromResult(true),
            "Deleted",
            onSuccess: () => { called = true; return Task.CompletedTask; });

        Assert.True(called);
        AssertToast(OmniSeverity.Success, "Saved", "Deleted");
    }

    [Fact]
    public async Task RunAsyncBool_False_ReturnsFalse()
    {
        var result = await _sut.RunAsync(
            () => Task.FromResult(false),
            "Deleted");

        Assert.False(result);
        AssertToast(OmniSeverity.Danger, "Error", "SaveFailed");
    }

    [Fact]
    public async Task RunAsyncBool_False_DoesNotInvokeOnSuccess()
    {
        var called = false;

        await _sut.RunAsync(
            () => Task.FromResult(false),
            "Deleted",
            onSuccess: () => { called = true; return Task.CompletedTask; });

        Assert.False(called);
        AssertToast(OmniSeverity.Danger, "Error", "SaveFailed");
    }

    private void AssertToast(OmniSeverity severity, string summary, string detail)
    {
        var message = Assert.Single(_overlay.Toasts());
        Assert.Equal(severity, message.Severity);
        Assert.Equal(summary, message.Summary);
        Assert.Equal(detail, message.Detail);
    }
}
