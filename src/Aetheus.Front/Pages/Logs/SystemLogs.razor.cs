// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Logs;

public partial class SystemLogs : ComponentBase, IAsyncDisposable
{
    [Parameter, SupplyParameterFromQuery(Name = "search")]
    public string? Search { get; set; }

    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private NotificationService Notification { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private ClipboardService Clipboard { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;

    private List<SystemLogFileDto>? _logFiles;
    private List<string> _fileNames = [];
    private PaginatedResult<SystemLogEntryDto>? _entries;
    // LM3K: visible entries accumulate across pages ("load more"); _entries holds the latest page's
    // metadata (HasNext / TotalCount) used to drive the load-more control.
    private readonly List<SystemLogEntryDto> _accumulated = [];

    private string? _selectedFile;
    private string? _selectedLevel;
    private string? _searchText;
    private DateTime? _dateFrom;
    private DateTime? _dateTo;
    private int _page = 1;
    private bool _isLoading;
    private bool _isLoadingMore;
    private bool _isDownloading;
    private bool _isExporting;
    private bool _initialized;
    private string? _appliedSearch;

    private bool _autoRefresh;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private CancellationTokenSource? _autoRefreshCts;
    private CancellationTokenSource? _debounceCts;
    private Task? _autoRefreshTask;
    private Task? _debounceTask;
    private int _loadGeneration;
    private bool _disposed;
    private const int AutoRefreshIntervalMs = 10_000;
    private const int DebounceDelayMs = 400;

    private static readonly string[] LevelsArray = ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"];
    private List<LogLevelOption> _levelOptions = [];

    private readonly record struct LogLevelOption(string Text, string Value);

    protected override async Task OnInitializedAsync()
    {
        _searchText = Search;
        _appliedSearch = Search;
        _levelOptions = LevelsArray.Select(value => new LogLevelOption(L[value], value)).ToList();
        try
        {
            await LoadLogFilesAsync();
            await LoadEntriesAsync();
        }
        catch (HttpRequestException) { } // 401 on expired JWT - redirect handled by AuthProvider
        finally
        {
            _initialized = true;
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        if (!_initialized || string.Equals(Search, _appliedSearch, StringComparison.Ordinal))
            return;

        _appliedSearch = Search;
        _searchText = Search;
        await LoadEntriesAsync();
    }

    private async Task LoadLogFilesAsync()
    {
        _logFiles = await Api.Security.GetSystemLogFilesAsync();
        _fileNames = _logFiles?.Select(f => f.FileName).ToList() ?? [];
    }

    private async Task LoadEntriesAsync()
    {
        var generation = Interlocked.Increment(ref _loadGeneration);
        var selectedFile = _selectedFile;
        var selectedLevel = _selectedLevel;
        var searchText = _searchText;
        var dateFrom = _dateFrom;
        var dateTo = _dateTo;
        _isLoading = true;
        StateHasChanged();
        await _reloadLock.WaitAsync();
        try
        {
            var entries = await Api.Security.GetSystemLogEntriesAsync(
                selectedFile, selectedLevel, searchText, dateFrom, dateTo, 1);
            if (_disposed || generation != _loadGeneration) return;

            _page = 1;
            _entries = entries;
            _accumulated.Clear();
            if (entries is not null)
                _accumulated.AddRange(entries.Items);
        }
        finally
        {
            _reloadLock.Release();
            if (!_disposed && generation == _loadGeneration)
            {
                _isLoading = false;
                StateHasChanged();
            }
        }
    }

    // LM3K: fetch the next page and append it to the visible list - the grid stays in place (no full
    // reload / scroll reset), unlike the filter/refresh path which rebuilds from page 1.
    private async Task LoadMoreAsync()
    {
        if (_entries is null || !_entries.HasNext || _isLoadingMore) return;
        var generation = _loadGeneration;
        var nextPage = _page + 1;
        var selectedFile = _selectedFile;
        var selectedLevel = _selectedLevel;
        var searchText = _searchText;
        var dateFrom = _dateFrom;
        var dateTo = _dateTo;
        _isLoadingMore = true;
        StateHasChanged();
        await _reloadLock.WaitAsync();
        try
        {
            if (generation != _loadGeneration) return;
            var next = await Api.Security.GetSystemLogEntriesAsync(
                selectedFile, selectedLevel, searchText, dateFrom, dateTo, nextPage);
            if (!_disposed && generation == _loadGeneration && next is not null)
            {
                _page = nextPage;
                _entries = next;
                _accumulated.AddRange(next.Items);
            }
        }
        catch (HttpRequestException)
        {
            if (!_disposed && generation == _loadGeneration)
                Toast.Error("Error", "LoadFailed");
        }
        finally
        {
            _reloadLock.Release();
            if (!_disposed)
            {
                _isLoadingMore = false;
                StateHasChanged();
            }
        }
    }

    private async Task OnFilterChangedAsync()
    {
        _page = 1;
        await LoadEntriesAsync();
    }

    private async Task OnRefreshAsync()
    {
        await LoadLogFilesAsync();
        await LoadEntriesAsync();
    }

    // LM3K: the periodic auto-refresh must NOT snap the list back to page 1 and wipe the pages the
    // user accumulated via "load more". It re-fetches pages 1.._page and rebuilds the visible list,
    // preserving the accumulated depth while still surfacing the newest entries.
    private async Task RefreshPreservingDepthAsync()
    {
        var generation = Interlocked.Increment(ref _loadGeneration);
        var selectedFile = _selectedFile;
        var selectedLevel = _selectedLevel;
        var searchText = _searchText;
        var dateFrom = _dateFrom;
        var dateTo = _dateTo;
        var pagesToLoad = Math.Max(1, _page);
        await _reloadLock.WaitAsync();
        try
        {
            await LoadLogFilesAsync();
            var rebuilt = new List<SystemLogEntryDto>();
            PaginatedResult<SystemLogEntryDto>? lastPage = null;
            var fetched = 0;
            for (var p = 1; p <= pagesToLoad; p++)
            {
                var pageResult = await Api.Security.GetSystemLogEntriesAsync(
                    selectedFile, selectedLevel, searchText, dateFrom, dateTo, p);
                if (pageResult is null) break;
                lastPage = pageResult;
                rebuilt.AddRange(pageResult.Items);
                fetched = p;
                if (!pageResult.HasNext) break;
            }
            if (!_disposed && generation == _loadGeneration && lastPage is not null)
            {
                _entries = lastPage;
                _page = Math.Max(1, fetched);
                _accumulated.Clear();
                _accumulated.AddRange(rebuilt);
            }
            if (!_disposed && generation == _loadGeneration) StateHasChanged();
        }
        catch (HttpRequestException) { } // 401 on expired JWT during a background tick - redirect handled by AuthProvider
        finally { _reloadLock.Release(); }
    }

    private async Task OnSearchTextChanged(string value)
    {
        _searchText = value;
        _debounceCts?.Cancel();
        if (_debounceTask is not null) await _debounceTask;
        _debounceCts?.Dispose();
        _debounceCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _debounceTask = RunDebounceAsync(_debounceCts.Token);
    }

    private async Task ToggleAutoRefresh()
    {
        _autoRefresh = !_autoRefresh;
        _autoRefreshCts?.Cancel();
        if (_autoRefreshTask is not null) await _autoRefreshTask;
        _autoRefreshCts?.Dispose();
        _autoRefreshCts = null;
        if (!_autoRefresh) return;

        _autoRefreshCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _autoRefreshTask = RunAutoRefreshAsync(_autoRefreshCts.Token);
    }

    private async Task RunDebounceAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(DebounceDelayMs, ct);
            await InvokeAsync(LoadEntriesAsync);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private async Task RunAutoRefreshAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(AutoRefreshIntervalMs));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await InvokeAsync(RefreshPreservingDepthAsync);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private async Task OnDownloadAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedFile)) return;
        _isDownloading = true;
        StateHasChanged();
        try
        {
            var bytes = await Api.Security.DownloadSystemLogFileAsync(_selectedFile);
            if (bytes is not null)
                await JS.InvokeVoidAsync("downloadFileFromBytes", bytes, _selectedFile, "text/plain");
            else
                Toast.Error("Error", "DownloadFailed");
        }
        finally
        {
            _isDownloading = false;
            StateHasChanged();
        }
    }

    private async Task OnExportCsvAsync()
    {
        _isExporting = true;
        StateHasChanged();
        try
        {
            var bytes = await Api.Security.ExportSystemLogsCsvAsync(_selectedFile, _selectedLevel, _searchText, _dateFrom, _dateTo);
            if (bytes is not null)
            {
                var fileName = $"system-logs-{DateTime.Now:yyyyMMdd-HHmmss}.csv";
                await JS.InvokeVoidAsync("downloadFileFromBytes", bytes, fileName, "text/csv");
            }
            else
            {
                Toast.Error("Error", "ExportFailed");
            }
        }
        finally
        {
            _isExporting = false;
            StateHasChanged();
        }
    }

    private async Task OnPurgeAsync()
    {
        var confirmed = await Dialog.Confirm(
            L["PurgeConfirmMessage"],
            L["PurgeLogs"],
            new ConfirmOptions { OkButtonText = L["Confirm"], CancelButtonText = L["Cancel"] });
        if (confirmed != true) return;

        var deleted = await Api.Security.PurgeSystemLogsAsync();
        if (deleted.HasValue)
        {
            Notification.Notify(NotificationSeverity.Success, L["PurgeLogs"],
                string.Format(L["PurgeResult"], deleted.Value));
            await OnRefreshAsync();
        }
    }

    private Task OnCopyCorrelationIdAsync(string correlationId) => Clipboard.CopyAsync(correlationId, correlationId);

    private Task OnCopyMessageAsync(string message) => Clipboard.CopyAsync(message, L["MessageCopied"]);

    // RC9T: the whole row opens the message dialog (the explicit "view" button stays too); action
    // buttons stop propagation so a copy click doesn't also pop the dialog.
    private Task OnRowClick(DataGridRowMouseEventArgs<SystemLogEntryDto> args) =>
        args.Data is { } entry ? ShowMessageDialogAsync(entry) : Task.CompletedTask;

    private async Task ShowMessageDialogAsync(SystemLogEntryDto entry)
    {
        await Dialog.OpenAsync<LogEntryDialog>(L["Message"],
            new Dictionary<string, object?>
            {
                ["Message"] = entry.Message,
                ["Exception"] = entry.Exception ?? string.Empty
            },
            new DialogOptions { Width = "700px", CloseDialogOnOverlayClick = true, AutoFocusFirstElement = false });
    }

    private static BadgeStyle GetBadgeStyle(string level) => level switch
    {
        "Error" or "Fatal" => BadgeStyle.Danger,
        "Warning" => BadgeStyle.Warning,
        "Information" => BadgeStyle.Info,
        "Debug" => BadgeStyle.Light,
        _ => BadgeStyle.Light
    };

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        Interlocked.Increment(ref _loadGeneration);
        _lifetimeCts.Cancel();
        _autoRefreshCts?.Cancel();
        _debounceCts?.Cancel();

        var tasks = new[] { _autoRefreshTask, _debounceTask }
            .Where(task => task is not null)
            .Cast<Task>()
            .ToArray();
        if (tasks.Length > 0) await Task.WhenAll(tasks);

        _autoRefreshCts?.Dispose();
        _debounceCts?.Dispose();
        _lifetimeCts.Dispose();
        _reloadLock.Dispose();
    }
}
