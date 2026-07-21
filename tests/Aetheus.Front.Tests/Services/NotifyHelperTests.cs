// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Microsoft.Extensions.Localization;
using NSubstitute;
using Radzen;

namespace Aetheus.Front.Tests;

public class NotifyHelperTests
{
    private readonly NotificationService _notification = new();
    private readonly IStringLocalizer<AppStrings> _localizer = Substitute.For<IStringLocalizer<AppStrings>>();
    private readonly NotifyHelper _sut;

    public NotifyHelperTests()
    {
        // Localizer echoes the key as its value, so assertions can match on the keys passed in.
        _localizer[Arg.Any<string>()]
            .Returns(ci => new LocalizedString((string)ci[0], (string)ci[0]));
        _localizer[Arg.Any<string>(), Arg.Any<object[]>()]
            .Returns(ci => new LocalizedString((string)ci[0], (string)ci[0]));
        _sut = new NotifyHelper(_notification, _localizer);
    }

    [Fact]
    public void Success_WithTitleAndMessage_QueuesSuccessNotification()
    {
        _sut.Success("SuccessTitle", "SuccessMessage");

        var msg = Assert.Single(_notification.Messages);
        Assert.Equal(NotificationSeverity.Success, msg.Severity);
        Assert.Equal("SuccessTitle", msg.Summary);
        Assert.Equal("SuccessMessage", msg.Detail);
    }

    [Fact]
    public void Success_WithArgs_FormatsDetail()
    {
        _sut.Success("SuccessTitle", "Value {0}", "arg1");

        var msg = Assert.Single(_notification.Messages);
        Assert.Equal(NotificationSeverity.Success, msg.Severity);
        Assert.Equal("Value arg1", msg.Detail);
    }

    [Fact]
    public void Error_WithTitleAndMessage_QueuesErrorNotification()
    {
        _sut.Error("ErrorTitle", "ErrorMessage");

        var msg = Assert.Single(_notification.Messages);
        Assert.Equal(NotificationSeverity.Error, msg.Severity);
        Assert.Equal("ErrorTitle", msg.Summary);
        Assert.Equal("ErrorMessage", msg.Detail);
    }

    [Fact]
    public void Error_WithArgs_FormatsDetail()
    {
        _sut.Error("ErrorTitle", "{0} then {1}", "arg1", "arg2");

        var msg = Assert.Single(_notification.Messages);
        Assert.Equal(NotificationSeverity.Error, msg.Severity);
        Assert.Equal("arg1 then arg2", msg.Detail);
    }

    [Fact]
    public void Info_WithTitleAndMessage_QueuesInfoNotification()
    {
        _sut.Info("InfoTitle", "InfoMessage");

        var msg = Assert.Single(_notification.Messages);
        Assert.Equal(NotificationSeverity.Info, msg.Severity);
        Assert.Equal("InfoTitle", msg.Summary);
        Assert.Equal("InfoMessage", msg.Detail);
    }

    [Fact]
    public void Info_WithRawMessage_PassesDetailThrough()
    {
        _sut.Info("InfoTitle", "Raw info message");

        var msg = Assert.Single(_notification.Messages);
        Assert.Equal(NotificationSeverity.Info, msg.Severity);
        Assert.Equal("Raw info message", msg.Detail);
    }

    [Fact]
    public void Warning_WithTitleAndMessage_QueuesWarningNotification()
    {
        _sut.Warning("WarnTitle", "WarnMessage");

        var msg = Assert.Single(_notification.Messages);
        Assert.Equal(NotificationSeverity.Warning, msg.Severity);
        Assert.Equal("WarnTitle", msg.Summary);
        Assert.Equal("WarnMessage", msg.Detail);
    }

    [Fact]
    public void Warning_WithArgs_FormatsDetail()
    {
        _sut.Warning("WarnTitle", "{0}-{1}", "a", "b");

        var msg = Assert.Single(_notification.Messages);
        Assert.Equal(NotificationSeverity.Warning, msg.Severity);
        Assert.Equal("a-b", msg.Detail);
    }

    [Fact]
    public void Notify_WithSeverityTitleAndRawMessage_HonorsSeverity()
    {
        _sut.Notify(NotificationSeverity.Info, "Title", "Raw message");

        var msg = Assert.Single(_notification.Messages);
        Assert.Equal(NotificationSeverity.Info, msg.Severity);
        Assert.Equal("Title", msg.Summary);
        Assert.Equal("Raw message", msg.Detail);
    }

    [Fact]
    public void Success_SummaryOnly_QueuesSuccessWithSummary()
    {
        _sut.Success("SuccessSummary");

        var msg = Assert.Single(_notification.Messages);
        Assert.Equal(NotificationSeverity.Success, msg.Severity);
        Assert.Equal("SuccessSummary", msg.Summary);
    }

    [Fact]
    public void Error_SummaryOnly_QueuesErrorWithSummary()
    {
        _sut.Error("ErrorSummary");

        var msg = Assert.Single(_notification.Messages);
        Assert.Equal(NotificationSeverity.Error, msg.Severity);
        Assert.Equal("ErrorSummary", msg.Summary);
    }
}
