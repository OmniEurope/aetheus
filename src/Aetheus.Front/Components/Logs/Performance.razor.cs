// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace Aetheus.Front.Components.Logs;

public partial class Performance : IAsyncDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private ClientApiTimings BrowserTimings { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private HubConnectionFactory HubFactory { get; set; } = default!;

    /// <summary>Calls of this session shown, slowest first.</summary>
    private const int BrowserCallsShown = 20;

    private ApiPerformanceReportDto? _report;
    private List<ClientApiCall> _browserCalls = [];
    private AetheusDataGrid<ApiCallTimingDto>? _slowestGrid;
    private AetheusDataGrid<ApiEndpointTimingDto>? _endpointsGrid;
    private AetheusDataGrid<ClientApiCall>? _browserGrid;

    /// <summary>Recette R-210: the date column shows local time, so its range filter and sort read the
    /// same local instant rather than the UTC one the API sends.</summary>
    private static readonly Func<ApiCallTimingDto, object?> LocalAt = call => call.At.ToLocalTime();
    private bool _loadFailed;

    // R-181: replaces the Refresh button. The backend announces new samples on the admin hub
    // (AdminEntities.ApiPerformance, coalesced server-side); bursts are coalesced here too.
    private AdminEntitySubscription? _realtime;
    private readonly TrailingReloadCoalescer _realtimeReload = new(500);

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
        _realtime = new AdminEntitySubscription(HubFactory);
        // Not awaited: the figures are on screen now, a slow hub negotiation must not hold them back.
        _ = _realtime.StartAsync(AdminEntities.ApiPerformance,
            () => InvokeAsync(() => _realtimeReload.RequestAsync(ReloadFromPushAsync)));
    }

    private async Task LoadAsync()
    {
        _loadFailed = false;
        try
        {
            _report = await Api.Security.GetApiPerformanceAsync() ?? new ApiPerformanceReportDto();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _loadFailed = true;
        }
        finally
        {
            SnapshotBrowserCalls();
        }
    }

    /// <summary>A push re-reads the report in place; a failure keeps the figures on screen for the next push.</summary>
    internal async Task ReloadFromPushAsync()
    {
        try
        {
            var report = await Api.Security.GetApiPerformanceAsync() ?? new ApiPerformanceReportDto();
            // Recette R-227: the grids note the rows they hold before the new ones land, so a new
            // sample reads bold for a few seconds.
            if (_slowestGrid is not null) await _slowestGrid.Refresh();
            if (_endpointsGrid is not null) await _endpointsGrid.Refresh();
            if (_browserGrid is not null) await _browserGrid.Refresh();
            _report = report;
            _loadFailed = false;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return;
        }
        SnapshotBrowserCalls();
        StateHasChanged();
    }

    private void SnapshotBrowserCalls() =>
        _browserCalls = [.. BrowserTimings.Snapshot()
            .OrderByDescending(call => call.DurationMs)
            .Take(BrowserCallsShown)];

    /// <summary>Says how much the figures cover: 24 hours, or less since the instance started, and
    /// whether the cap dropped the oldest samples. A number without its window invites the wrong
    /// conclusion.</summary>
    private string WindowText => _report is { Since: { } since }
        ? string.Format(CultureInfo.CurrentCulture, L["PerformanceWindow"], _report.SampleCount, since.ToLocalTime().ToString("g"))
          + (_report.Truncated ? " " + L["PerformanceTruncated"] : string.Empty)
        : L["PerformanceNoSamples"];

    /// <summary>Recette R-454: the tab slugs, in the order of the tabs.</summary>
    private static readonly string[] TabSlugs = ["slowest", "routes", "browser"];

    /// <summary>
    /// Recette R-454: every row of the three tables in one Markdown file, raw, for an AI to read. Built
    /// when the button is pressed: the report on screen and every call this browser session kept (not
    /// only the slowest shown).
    /// </summary>
    private OmniMarkdownTableExport<PerformanceExportRow> CreateExport() =>
        PerformanceMarkdownExport.Create(_report ?? new ApiPerformanceReportDto(), BrowserTimings.Snapshot(), key => L[key].Value);

    private static string Milliseconds(double value) =>
        value >= 1000
            ? (value / 1000).ToString("0.00", CultureInfo.CurrentCulture) + " s"
            : Math.Round(value).ToString(CultureInfo.CurrentCulture) + " ms";

    private static string FormatBytes(long bytes) =>
        bytes >= 1024 * 1024
            ? (bytes / 1024d / 1024d).ToString("0.0", CultureInfo.CurrentCulture) + " MB"
            : bytes >= 1024
                ? (bytes / 1024d).ToString("0.0", CultureInfo.CurrentCulture) + " kB"
                : bytes.ToString(CultureInfo.CurrentCulture) + " B";

    public async ValueTask DisposeAsync()
    {
        if (_realtime is not null)
            await _realtime.DisposeAsync();
    }
}
