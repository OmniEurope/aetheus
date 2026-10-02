// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using OmniEurope.Blazor.Components;

namespace Aetheus.Front.Tests;

public class NotifyHelperTests
{
    private readonly OmniOverlayService _overlay = new(new FakeTimeProvider());
    private readonly IStringLocalizer<AppStrings> _localizer = Substitute.For<IStringLocalizer<AppStrings>>();
    private readonly NotifyHelper _sut;

    public NotifyHelperTests()
    {
        // Localizer echoes the key as its value, so assertions can match on the keys passed in.
        _localizer[Arg.Any<string>()]
            .Returns(ci => new LocalizedString((string)ci[0], (string)ci[0]));
        _localizer[Arg.Any<string>(), Arg.Any<object[]>()]
            .Returns(ci => new LocalizedString((string)ci[0], (string)ci[0]));
        _sut = new NotifyHelper(_overlay, _localizer);
    }

    [Fact]
    public void Success_WithTitleAndMessage_QueuesSuccessNotification()
    {
        _sut.Success("SuccessTitle", "SuccessMessage");

        var msg = Assert.Single(_overlay.Toasts());
        Assert.Equal(OmniSeverity.Success, msg.Severity);
        Assert.Equal("SuccessTitle", msg.Summary);
        Assert.Equal("SuccessMessage", msg.Detail);
    }

    [Fact]
    public void Success_WithArgs_FormatsDetail()
    {
        _sut.Success("SuccessTitle", "Value {0}", "arg1");

        var msg = Assert.Single(_overlay.Toasts());
        Assert.Equal(OmniSeverity.Success, msg.Severity);
        Assert.Equal("Value arg1", msg.Detail);
    }

    [Fact]
    public void Error_WithTitleAndMessage_QueuesErrorNotification()
    {
        _sut.Error("ErrorTitle", "ErrorMessage");

        var msg = Assert.Single(_overlay.Toasts());
        Assert.Equal(OmniSeverity.Danger, msg.Severity);
        Assert.Equal("ErrorTitle", msg.Summary);
        Assert.Equal("ErrorMessage", msg.Detail);
    }

    [Fact]
    public void Error_WithArgs_FormatsDetail()
    {
        _sut.Error("ErrorTitle", "{0} then {1}", "arg1", "arg2");

        var msg = Assert.Single(_overlay.Toasts());
        Assert.Equal(OmniSeverity.Danger, msg.Severity);
        Assert.Equal("arg1 then arg2", msg.Detail);
    }

    [Fact]
    public void Info_WithTitleAndMessage_QueuesInfoNotification()
    {
        _sut.Info("InfoTitle", "InfoMessage");

        var msg = Assert.Single(_overlay.Toasts());
        Assert.Equal(OmniSeverity.Info, msg.Severity);
        Assert.Equal("InfoTitle", msg.Summary);
        Assert.Equal("InfoMessage", msg.Detail);
    }

    [Fact]
    public void Info_WithRawMessage_PassesDetailThrough()
    {
        _sut.Info("InfoTitle", "Raw info message");

        var msg = Assert.Single(_overlay.Toasts());
        Assert.Equal(OmniSeverity.Info, msg.Severity);
        Assert.Equal("Raw info message", msg.Detail);
    }

    [Fact]
    public void Warning_WithTitleAndMessage_QueuesWarningNotification()
    {
        _sut.Warning("WarnTitle", "WarnMessage");

        var msg = Assert.Single(_overlay.Toasts());
        Assert.Equal(OmniSeverity.Warning, msg.Severity);
        Assert.Equal("WarnTitle", msg.Summary);
        Assert.Equal("WarnMessage", msg.Detail);
    }

    [Fact]
    public void Warning_WithArgs_FormatsDetail()
    {
        _sut.Warning("WarnTitle", "{0}-{1}", "a", "b");

        var msg = Assert.Single(_overlay.Toasts());
        Assert.Equal(OmniSeverity.Warning, msg.Severity);
        Assert.Equal("a-b", msg.Detail);
    }

    [Theory]
    [InlineData(OmniSeverity.Info)]
    [InlineData(OmniSeverity.Success)]
    [InlineData(OmniSeverity.Warning)]
    [InlineData(OmniSeverity.Danger)]
    public void Notify_WithOmniSeverity_HonorsSeverity(OmniSeverity severity)
    {
        _sut.Notify(severity, "Title", "Raw message");

        var msg = Assert.Single(_overlay.Toasts());
        Assert.Equal(severity, msg.Severity);
        Assert.Equal("Title", msg.Summary);
        Assert.Equal("Raw message", msg.Detail);
    }

    [Fact]
    public void Success_SummaryOnly_QueuesSuccessWithSummary()
    {
        _sut.Success("SuccessSummary");

        var msg = Assert.Single(_overlay.Toasts());
        Assert.Equal(OmniSeverity.Success, msg.Severity);
        Assert.Equal("SuccessSummary", msg.Summary);
        Assert.Equal(string.Empty, msg.Detail);
    }

    [Fact]
    public void Error_SummaryOnly_QueuesErrorWithSummary()
    {
        _sut.Error("ErrorSummary");

        var msg = Assert.Single(_overlay.Toasts());
        Assert.Equal(OmniSeverity.Danger, msg.Severity);
        Assert.Equal("ErrorSummary", msg.Summary);
        Assert.Equal(string.Empty, msg.Detail);
    }
}
