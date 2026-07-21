// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Reflection;
using Aetheus.Front.Pages;
using Bunit;
using LogsPage = Aetheus.Front.Pages.Logs.Logs;

namespace Aetheus.Front.Tests.Pages;

public class LogsTests : BunitContext
{
    public LogsTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    private readonly BunitTestHelper.TestHandler _handler;

    [Fact]
    public void Renders_LogsPage()
    {
        var cut = Render<LogsPage>();
        Assert.Contains("LiveLogs", cut.Markup);
    }

    [Fact]
    public void Renders_LogsPage_ContainsExpectedContent()
    {
        var cut = Render<LogsPage>();
        // No watch and no logs yet → the "select a task to watch" empty state is shown.
        Assert.Contains("SelectTaskToWatch", cut.Markup);
    }

    // LogsPage_RendersWithoutError_WhenAuthenticated removed: DoesNotContain("error", ...)
    // over the whole markup is a near-tautology that passes for any page not literally
    // printing "error". The positive render is covered by Renders_LogsPage / LiveLogs above.

    [Fact]
    public void LogsPage_ContainsLogViewElements()
    {
        var cut = Render<LogsPage>();
        // The live-log panel container and the Watch/Clear action buttons always render.
        Assert.NotEmpty(cut.FindAll(".live-log-panel"));
        Assert.Contains("Watch", cut.Markup);
    }

    [Fact]
    public void LogsPage_CanRenderMultipleTimes()
    {
        var cut1 = Render<LogsPage>();
        var cut2 = Render<LogsPage>();
        // Two independent renders both produce the live-logs heading - no shared-state bleed.
        Assert.Contains("LiveLogs", cut1.Markup);
        Assert.Contains("LiveLogs", cut2.Markup);
    }

    [Fact]
    public async Task StartWatching_WithNullFilter_DoesNothing()
    {
        var cut = Render<LogsPage>();

        cut.Instance._taskIdFilter = null;

        await cut.Instance.StartWatching();

        // A null filter short-circuits before any hub join: no watch is established.
        var watchId = typeof(LogsPage)
            .GetField("_currentWatchId", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance);
        Assert.Null(watchId);
    }

    [Fact]
    public async Task DisposeAsync_WithNullHub_DoesNotThrow()
    {
        var cut = Render<LogsPage>();
        // No hub was ever created (StartWatching not called), so disposal is a safe no-op,
        // and a second dispose stays exception-free.
        await ((IAsyncDisposable)cut.Instance).DisposeAsync();
        var ex = await Record.ExceptionAsync(async () => await ((IAsyncDisposable)cut.Instance).DisposeAsync());
        Assert.Null(ex);
    }

    [Theory]
    [InlineData("hello ***", "hello <span class=\"masked-value\">***</span>")]
    [InlineData("no mask", "no mask")]
    [InlineData("*** start", "<span class=\"masked-value\">***</span> start")]
    public void HighlightMasked_ReplacesTripleStars(string input, string expected)
    {
        var method = typeof(LogsPage).GetMethod("HighlightMasked", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var result = (string)method.Invoke(null, [input])!;
        Assert.Equal(expected, result);
    }

    [Fact]
    public void HighlightMasked_EncodesHtml()
    {
        var method = typeof(LogsPage).GetMethod("HighlightMasked", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var result = (string)method.Invoke(null, ["<script>alert('xss')</script>"])!;
        Assert.DoesNotContain("<script>", result);
        Assert.Contains("&lt;script&gt;", result);
    }

    [Fact]
    public void Renders_LiveLogsHeading()
    {
        var cut = Render<LogsPage>();
        Assert.Contains("LiveLogs", cut.Markup);
    }

    [Fact]
    public void Renders_TaskIdFilterInput()
    {
        var cut = Render<LogsPage>();
        // Page should have an input for task ID filter
        var inputs = cut.FindAll("input");
        Assert.True(inputs.Count >= 1);
    }

    [Fact]
    public async Task ToggleUnmasked_WithNullCurrentWatch_DoesNothing()
    {
        var cut = Render<LogsPage>();

        var method = typeof(LogsPage).GetMethod("ToggleUnmasked", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(cut.Instance, [])!;

        var showUnmasked = (bool)typeof(LogsPage)
            .GetField("_showUnmasked", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        Assert.False(showUnmasked);
    }

    [Fact]
    public void ResetWatchState_ClearsPriorLogsAndMaskedMode()
    {
        var cut = Render<LogsPage>();
        var logs = (List<Aetheus.Shared.DTOs.TaskLogDto>)typeof(LogsPage)
            .GetField("_logs", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        logs.Add(new Aetheus.Shared.DTOs.TaskLogDto { TaskId = 1, Message = "old" });
        typeof(LogsPage).GetField("_showUnmasked", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(cut.Instance, true);

        typeof(LogsPage).GetMethod("ResetWatchState", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(cut.Instance, [2]);

        Assert.Empty(logs);
        Assert.False((bool)typeof(LogsPage).GetField("_showUnmasked", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance)!);
        Assert.Equal(2, typeof(LogsPage).GetField("_currentWatchId", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance));
    }

    [Fact]
    public void AppendLog_IgnoresMessagesFromPreviousWatch()
    {
        var cut = Render<LogsPage>();
        typeof(LogsPage).GetMethod("ResetWatchState", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(cut.Instance, [2]);
        var append = typeof(LogsPage).GetMethod("AppendLog", BindingFlags.NonPublic | BindingFlags.Instance)!;

        append.Invoke(cut.Instance, [new Aetheus.Shared.DTOs.TaskLogDto { TaskId = 1, Message = "stale" }]);
        append.Invoke(cut.Instance, [new Aetheus.Shared.DTOs.TaskLogDto { TaskId = 2, Message = "current" }]);

        var logs = (List<Aetheus.Shared.DTOs.TaskLogDto>)typeof(LogsPage)
            .GetField("_logs", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        Assert.Equal("current", Assert.Single(logs).Message);
    }
}
