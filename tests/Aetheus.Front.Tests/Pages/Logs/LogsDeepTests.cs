// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Bunit;
using LogsPage = Aetheus.Front.Components.Logs.Logs;

namespace Aetheus.Front.Tests.Pages.Logs;

public class LogsDeepTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly BunitTestHelper.TestHandler _handler;

    public LogsDeepTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    [Fact]
    public void Renders_LogsPage()
    {
        var cut = Render<LogsPage>();
        Assert.Contains("LiveLogs", cut.Markup);
    }

    [Fact]
    public void Initial_State_HasNoLogs()
    {
        var cut = Render<LogsPage>();
        var logs = (List<TaskLogDto>)typeof(LogsPage).GetField("_logs", Priv)!.GetValue(cut.Instance)!;
        Assert.Empty(logs);
    }

    [Fact]
    public void TaskIdFilter_InitiallyNull()
    {
        var cut = Render<LogsPage>();
        var filter = (int?)typeof(LogsPage).GetField("_taskIdFilter", Priv)!.GetValue(cut.Instance);
        Assert.Null(filter);
    }

    [Fact]
    public async Task StartWatching_WithNullFilter_ProducesNothing()
    {
        var cut = Render<LogsPage>();
        // _taskIdFilter is null, so StartWatching returns immediately
        await cut.InvokeAsync(() => cut.Instance.StartWatching());
        var logs = (List<TaskLogDto>)typeof(LogsPage).GetField("_logs", Priv)!.GetValue(cut.Instance)!;
        Assert.Empty(logs);
    }

    [Fact]
    public void HighlightMasked_EncodesAndReplacesMaskedValues()
    {
        var method = typeof(LogsPage).GetMethod("HighlightMasked", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (string)method.Invoke(null, ["secret value is ***"])!;
        Assert.Contains("masked-value", result);
        Assert.Contains("***", result);
    }

    [Fact]
    public void HighlightMasked_NoMask_JustEncodes()
    {
        var method = typeof(LogsPage).GetMethod("HighlightMasked", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (string)method.Invoke(null, ["plain text <b>bold</b>"])!;
        Assert.Contains("&lt;b&gt;", result);
        Assert.DoesNotContain("masked-value", result);
    }

    [Fact]
    public async Task ToggleUnmasked_WithNoCurrentWatch_LeavesTheFlagOff()
    {
        var cut = Render<LogsPage>();
        // _currentWatchId is null, so ToggleUnmasked returns immediately
        var method = typeof(LogsPage).GetMethod("ToggleUnmasked", Priv)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [])!);
        // No current watch → the guard returns before flipping _showUnmasked or fetching logs.
        var showUnmasked = (bool)typeof(LogsPage)
            .GetField("_showUnmasked", Priv)!.GetValue(cut.Instance)!;
        Assert.False(showUnmasked);
    }

    [Fact]
    public void ShowUnmasked_InitiallyFalse()
    {
        var cut = Render<LogsPage>();
        var show = (bool)typeof(LogsPage).GetField("_showUnmasked", Priv)!.GetValue(cut.Instance)!;
        Assert.False(show);
    }
}
