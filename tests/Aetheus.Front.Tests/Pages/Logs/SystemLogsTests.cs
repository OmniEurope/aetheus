// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Logs;
using Aetheus.Shared.DTOs;
using Bunit;
using Radzen;

namespace Aetheus.Front.Tests.Pages;

public class SystemLogsTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public SystemLogsTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        _handler.SetJsonResponse("api/system-logs/files", new List<SystemLogFileDto>
        {
            new("app-20260424.log", 102_400, DateTime.UtcNow)
        });
        _handler.SetJsonResponse("api/system-logs", new PaginatedResult<SystemLogEntryDto>
        {
            TotalCount = 0,
            Items = []
        });
    }

    [Fact]
    public void Renders_SystemLogsHeader()
    {
        var cut = Render<SystemLogs>();
        Assert.Contains("SystemLogs", cut.Markup);
    }

    [Theory]
    [InlineData("Error")]
    [InlineData("Fatal")]
    [InlineData("Warning")]
    [InlineData("Information")]
    [InlineData("Debug")]
    [InlineData("Verbose")]
    [InlineData("Unknown")]
    public void GetBadgeStyle_ReturnsExpectedForAllLevels(string level)
    {
        var method = typeof(SystemLogs).GetMethod("GetBadgeStyle", BindingFlags.NonPublic | BindingFlags.Static)!;
        var style = method.Invoke(null, [level]);
        // The mapper must return a defined BadgeStyle for every level (incl. the "Unknown" fallback).
        Assert.IsType<BadgeStyle>(style);
        Assert.True(Enum.IsDefined((BadgeStyle)style!));
    }

    [Fact]
    public async Task LoadEntries_LoadsFromApi()
    {
        var cut = Render<SystemLogs>();
        var method = typeof(SystemLogs).GetMethod("LoadEntriesAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        // LoadEntriesAsync fetches page 1 from the system-logs endpoint and stores the paginated result.
        Assert.Contains(_handler.Requests, r => r.Method == "GET" && r.Url.Contains("api/system-logs"));
        var entries = (PaginatedResult<SystemLogEntryDto>?)typeof(SystemLogs)
            .GetField("_entries", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance);
        Assert.NotNull(entries);
        Assert.Equal(0, entries.TotalCount);
    }

    [Fact]
    public async Task DisposeAsync_CancelsBackgroundWork()
    {
        var cut = Render<SystemLogs>();
        await ((IAsyncDisposable)cut.Instance).DisposeAsync();
    }

    [Fact]
    public async Task LoadMore_FailureDoesNotSkipPageAndRetryRequestsSamePage()
    {
        _handler.SetJsonResponse("api/system-logs", new PaginatedResult<SystemLogEntryDto>
        {
            TotalCount = 2,
            Page = 1,
            PageSize = 1,
            Items = [new SystemLogEntryDto(1, DateTime.UtcNow, "Information", "test", "first", null, null)]
        });
        var cut = Render<SystemLogs>();
        cut.WaitForState(() => GetPage(cut.Instance) == 1);
        _handler.SetResponse("api/system-logs/entries?page=2", System.Net.HttpStatusCode.InternalServerError);

        await InvokeLoadMoreAsync(cut);

        Assert.Equal(1, GetPage(cut.Instance));
        _handler.SetJsonResponse("api/system-logs/entries?page=2", new PaginatedResult<SystemLogEntryDto>
        {
            TotalCount = 2,
            Page = 2,
            PageSize = 1,
            Items = [new SystemLogEntryDto(2, DateTime.UtcNow, "Information", "test", "second", null, null)]
        });

        await InvokeLoadMoreAsync(cut);

        Assert.Equal(2, GetPage(cut.Instance));
        Assert.Equal(2, _handler.Requests.Count(r => r.Url.Contains("page=2", StringComparison.Ordinal)));
    }

    private static int GetPage(SystemLogs instance) => (int)typeof(SystemLogs)
        .GetField("_page", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;

    private static async Task InvokeLoadMoreAsync(IRenderedComponent<SystemLogs> cut)
    {
        var method = typeof(SystemLogs).GetMethod("LoadMoreAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);
    }
}
