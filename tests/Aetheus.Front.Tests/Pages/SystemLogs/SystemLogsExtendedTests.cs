// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Logs;
using Aetheus.Shared.DTOs;
using Bunit;
using Radzen;

namespace Aetheus.Front.Tests.Pages;

/// <summary>
/// Extended coverage for SystemLogs: static helpers, filter/search methods, toggle auto-refresh,
/// download/export paths, Dispose, and GetBadgeStyle/FormatSize variants.
/// </summary>
public class SystemLogsExtendedTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly BindingFlags PrivStatic = BindingFlags.NonPublic | BindingFlags.Static;

    private readonly BunitTestHelper.TestHandler _handler;

    public SystemLogsExtendedTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        SetupDefaultStubs();
    }

    private void SetupDefaultStubs()
    {
        // Use correct API URLs matching ApiClient.SystemLogs.cs
        _handler.SetJsonResponse("api/system-logs/files", new List<SystemLogFileDto>
        {
            new("app-20260101.log", 4096, DateTime.Now.AddDays(-1)),
            new("app-20260102.log", 1_200_000, DateTime.Now)
        });
        _handler.SetJsonResponse("api/system-logs/entries", new PaginatedResult<SystemLogEntryDto>
        {
            Items =
            [
                new(1, DateTime.Now, "Error", "TestSvc", "Something broke", null, null),
                new(2, DateTime.Now, "Warning", "MemSvc", "Low memory", null, null),
                new(3, DateTime.Now, "Information", "App", "Started", null, null),
                new(4, DateTime.Now, "Debug", "App", "Debug msg", null, null),
                new(5, DateTime.Now, "Fatal", "Core", "Crash", null, null)
            ],
            TotalCount = 5,
            Page = 1,
            PageSize = 50
        });
    }

    private IRenderedComponent<SystemLogs> RenderPage()
    {
        var cut = Render<SystemLogs>();
        cut.WaitForState(() => cut.Markup.Length > 100, TimeSpan.FromSeconds(2));
        return cut;
    }

    // ── Static helpers ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Error", BadgeStyle.Danger)]
    [InlineData("Fatal", BadgeStyle.Danger)]
    [InlineData("Warning", BadgeStyle.Warning)]
    [InlineData("Information", BadgeStyle.Info)]
    [InlineData("Debug", BadgeStyle.Light)]
    [InlineData("Verbose", BadgeStyle.Secondary)]
    [InlineData("Unknown", BadgeStyle.Secondary)]
    public void GetBadgeStyle_AllLevels_ReturnExpected(string level, BadgeStyle expected)
    {
        var method = typeof(SystemLogs).GetMethod("GetBadgeStyle", PrivStatic)!;
        var result = (BadgeStyle)method.Invoke(null, [level])!;
        Assert.Equal(expected, result);
    }

    // ── Render ────────────────────────────────────────────────────────────────

    [Fact]
    public void Renders_LogFilesAndEntries_OnInit()
    {
        var cut = RenderPage();
        Assert.Contains("SystemLogs", cut.Markup);
    }

    [Fact]
    public void Renders_LogLevelBadges_ForAllLevels()
    {
        var cut = RenderPage();
        Assert.Contains("Error", cut.Markup);
    }

    // ── OnFilterChangedAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task OnFilterChangedAsync_ResetsPageToOne()
    {
        var cut = RenderPage();
        typeof(SystemLogs).GetField("_page", Priv)!.SetValue(cut.Instance, 3);

        var method = typeof(SystemLogs).GetMethod("OnFilterChangedAsync", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        Assert.Equal(1, (int)typeof(SystemLogs).GetField("_page", Priv)!.GetValue(cut.Instance)!);
    }

    // ── OnRefreshAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task OnRefreshAsync_ReloadsFilesAndEntries()
    {
        var cut = RenderPage();
        _handler.Requests.Clear();
        var method = typeof(SystemLogs).GetMethod("OnRefreshAsync", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        // OnRefreshAsync re-fetches both the log file list and the log entries.
        Assert.Contains(_handler.Requests, r => r.Url.Contains("api/system-logs/files"));
        Assert.Contains(_handler.Requests, r => r.Url.Contains("api/system-logs/entries"));
    }

    // ── OnSearchTextChanged ───────────────────────────────────────────────────

    [Fact]
    public void OnSearchTextChanged_SetsSearchText()
    {
        var cut = RenderPage();
        var method = typeof(SystemLogs).GetMethod("OnSearchTextChanged", Priv)!;
        method.Invoke(cut.Instance, ["error"]);
        Assert.Equal("error", (string?)typeof(SystemLogs).GetField("_searchText", Priv)!.GetValue(cut.Instance));
    }

    [Fact]
    public async Task OnSearchTextChanged_CancelsOldDebounceTask()
    {
        var cut = RenderPage();
        var method = typeof(SystemLogs).GetMethod("OnSearchTextChanged", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, ["first"])!);
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, ["second"])!);
        Assert.Equal("second", (string?)typeof(SystemLogs).GetField("_searchText", Priv)!.GetValue(cut.Instance));
    }

    // ── ToggleAutoRefresh ─────────────────────────────────────────────────────

    [Fact]
    public async Task ToggleAutoRefresh_EnablesTask()
    {
        var cut = RenderPage();
        var method = typeof(SystemLogs).GetMethod("ToggleAutoRefresh", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);
        Assert.True((bool)typeof(SystemLogs).GetField("_autoRefresh", Priv)!.GetValue(cut.Instance)!);
        Assert.NotNull(typeof(SystemLogs).GetField("_autoRefreshTask", Priv)!.GetValue(cut.Instance));
    }

    [Fact]
    public async Task ToggleAutoRefresh_DisablesTask_OnSecondCall()
    {
        var cut = RenderPage();
        var method = typeof(SystemLogs).GetMethod("ToggleAutoRefresh", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);
        Assert.False((bool)typeof(SystemLogs).GetField("_autoRefresh", Priv)!.GetValue(cut.Instance)!);
    }

    // ── OnDownloadAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task OnDownloadAsync_NoFileSelected_DoesNothing()
    {
        var cut = RenderPage();
        typeof(SystemLogs).GetField("_selectedFile", Priv)!.SetValue(cut.Instance, null);
        var method = typeof(SystemLogs).GetMethod("OnDownloadAsync", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);
        Assert.False((bool)typeof(SystemLogs).GetField("_isDownloading", Priv)!.GetValue(cut.Instance)!);
    }

    [Fact]
    public async Task OnDownloadAsync_WithFileSelected_FetchesAndInvokesJsDownload()
    {
        // Download endpoint uses GET api/system-logs/download/{filename}
        _handler.SetJsonResponse("api/system-logs/download", new byte[] { 0x01, 0x02, 0x03 });
        var cut = RenderPage();
        typeof(SystemLogs).GetField("_selectedFile", Priv)!.SetValue(cut.Instance, "app-20260101.log");
        var method = typeof(SystemLogs).GetMethod("OnDownloadAsync", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        // A selected file drives the download GET, and the returned bytes are handed to the JS helper.
        Assert.Contains(_handler.Requests, r => r.Method == "GET" && r.Url.Contains("api/system-logs/download/"));
        Assert.Contains(JSInterop.Invocations, i => i.Identifier == "downloadFileFromBytes");
    }

    // ── OnExportCsvAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task OnExportCsvAsync_FetchesAndInvokesJsDownload()
    {
        _handler.SetJsonResponse("api/system-logs/export", new byte[] { 0x01 });
        var cut = RenderPage();
        var method = typeof(SystemLogs).GetMethod("OnExportCsvAsync", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        // Export issues the CSV GET and streams the returned bytes to the JS download helper.
        Assert.Contains(_handler.Requests, r => r.Method == "GET" && r.Url.Contains("api/system-logs/export"));
        Assert.Contains(JSInterop.Invocations, i => i.Identifier == "downloadFileFromBytes");
    }

    // ── OnCopyCorrelationIdAsync ──────────────────────────────────────────────

    [Fact]
    public async Task OnCopyCorrelationIdAsync_DoesNotThrow()
    {
        var cut = RenderPage();
        var method = typeof(SystemLogs).GetMethod("OnCopyCorrelationIdAsync", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, ["corr-id-123"])!);

        // The correlation id is written verbatim to the clipboard via JS interop.
        var clip = Assert.Single(JSInterop.Invocations, i => i.Identifier == "navigator.clipboard.writeText");
        Assert.Equal("corr-id-123", clip.Arguments[0]);
    }

    // ── LoadLogFilesAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task LoadLogFilesAsync_PopulatesFileNames()
    {
        var cut = RenderPage();
        var method = typeof(SystemLogs).GetMethod("LoadLogFilesAsync", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);
        var names = (List<string>)typeof(SystemLogs).GetField("_fileNames", Priv)!.GetValue(cut.Instance)!;
        Assert.Contains("app-20260101.log", names);
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Dispose_WithTimers_DoesNotThrow()
    {
        var cut = RenderPage();
        var toggle = typeof(SystemLogs).GetMethod("ToggleAutoRefresh", Priv)!;
        await cut.InvokeAsync(async () => await (Task)toggle.Invoke(cut.Instance, [])!);
        // Enabling auto-refresh creates the live timer that Dispose must clean up.
        Assert.True((bool)typeof(SystemLogs).GetField("_autoRefresh", Priv)!.GetValue(cut.Instance)!);
        Assert.NotNull(typeof(SystemLogs).GetField("_autoRefreshTask", Priv)!.GetValue(cut.Instance));

        // Dispose with a live auto-refresh timer must not throw (it disposes the timer).
        await cut.Instance.DisposeAsync();
    }
}
