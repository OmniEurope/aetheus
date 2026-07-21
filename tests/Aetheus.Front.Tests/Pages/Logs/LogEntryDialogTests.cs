// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Logs;
using Aetheus.Front.Tests.TestDoubles;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Radzen;

namespace Aetheus.Front.Tests.Pages.Logs;

public class LogEntryDialogTests : BunitContext
{
    public LogEntryDialogTests() => BunitTestHelper.RegisterServices(this);

    [Fact]
    public void Renders_MessageAndException()
    {
        var cut = Render<LogEntryDialog>(p => p
            .Add(c => c.Message, "Something went boom")
            .Add(c => c.Exception, "System.NullReferenceException: ref"));

        Assert.Contains("Something went boom", cut.Markup);
        Assert.Contains("NullReferenceException", cut.Markup);
    }

    [Fact]
    public void Renders_MessageOnly_WithoutExceptionSection()
    {
        var cut = Render<LogEntryDialog>(p => p.Add(c => c.Message, "plain message"));

        Assert.Contains("plain message", cut.Markup);
        Assert.DoesNotContain("ExceptionDetails", cut.Markup);
    }

    [Fact]
    public void Copy_WithException_WritesMessageAndExceptionToClipboard()
    {
        var cut = Render<LogEntryDialog>(p => p
            .Add(c => c.Message, "boom")
            .Add(c => c.Exception, "stack-trace-here"));

        // First button in the action row is "Copy".
        cut.FindAll("button")[0].Click();

        // The clipboard JS interop was invoked with message + exception joined.
        JSInterop.VerifyInvoke("navigator.clipboard.writeText");
        var invocation = JSInterop.Invocations["navigator.clipboard.writeText"].Single();
        Assert.Equal("boom\n\nstack-trace-here", invocation.Arguments[0]);
    }

    [Fact]
    public void Copy_MessageOnly_WritesMessageToClipboard()
    {
        var cut = Render<LogEntryDialog>(p => p.Add(c => c.Message, "just the message"));

        cut.FindAll("button")[0].Click();

        JSInterop.VerifyInvoke("navigator.clipboard.writeText");
        var invocation = JSInterop.Invocations["navigator.clipboard.writeText"].Single();
        Assert.Equal("just the message", invocation.Arguments[0]);
    }

    [Fact]
    public void Close_InvokesDialogClose()
    {
        Services.AddSingleton<DialogService>(sp => new SpyDialogService(
            sp.GetRequiredService<NavigationManager>(),
            sp.GetRequiredService<IJSRuntime>()));

        var cut = Render<LogEntryDialog>(p => p.Add(c => c.Message, "boom"));
        var spy = (SpyDialogService)Services.GetRequiredService<DialogService>();

        Assert.False(spy.Closed);
        // Second button in the action row is "Close".
        cut.FindAll("button")[1].Click();

        Assert.True(spy.Closed);
    }
}
