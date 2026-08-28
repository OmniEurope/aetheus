// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Shared;

public partial class AppWebAnalyticsView
{
    [Inject] protected ApiClient Api { get; set; } = default!;
    [Inject] protected IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter] public int AppId { get; set; }

    protected sealed record SummaryTile(string Label, string Value);
    protected sealed record DailyPoint(string Label, int UniqueVisitors, long PageViews);

    protected AppWebAnalyticsSummaryDto? _summary;
    protected bool _loading = true;
    protected bool _loadFailed;
    private int _lastAppId = -1;

    protected IReadOnlyList<SummaryTile> SummaryTiles =>
        _summary is null
            ? []
            :
            [
                new(L["UniqueVisitorsToday"], _summary.UniqueVisitorsToday.ToString("N0")),
                new(L["UniqueVisitorsThisWeek"], _summary.UniqueVisitorsThisWeek.ToString("N0")),
                new(L["UniqueVisitorsThisMonth"], _summary.UniqueVisitorsThisMonth.ToString("N0")),
                new(L["SessionsThisMonth"], _summary.SessionsThisMonth.ToString("N0")),
                new(L["ReturningVisitorsThisMonth"], _summary.ReturningVisitorsThisMonth.ToString("N0")),
                new(L["PageViewsThisMonth"], _summary.PageViewsThisMonth.ToString("N0")),
                new(L["AuthenticatedUniqueThisMonth"], _summary.AuthenticatedUniqueThisMonth.ToString("N0")),
                new(
                    L["BrowserPerformanceSamplesLast30Days"],
                    _summary.BrowserPerformanceSamplesLast30Days.ToString("N0")),
                new(
                    L["AverageBrowserNavigationDuration"],
                    FormatDuration(_summary.AverageBrowserNavigationDurationMs)),
                new(
                    L["P95BrowserNavigationDuration"],
                    FormatDuration(_summary.P95BrowserNavigationDurationMs)),
                new(L["BrowserErrorsLast30Days"], _summary.BrowserErrorsLast30Days.ToString("N0"))
            ];

    protected IReadOnlyList<DailyPoint> DailyPoints =>
        _summary?.Daily.Select(point =>
            new DailyPoint(
                point.DayUtc.ToString("MM-dd"),
                point.UniqueVisitors,
                point.PageViews)).ToList() ?? [];

    protected string LastIngestText => _summary?.LastIngestAtUtc is { } receivedAt
        ? string.Format(L["LastAnalyticsIngest"], receivedAt.ToLocalTime().ToString("g"))
        : L["NoAnalyticsIngestYet"];

    protected string QuotaText => string.Format(
        L["AnalyticsStorageQuota"],
        _summary?.StorageUsagePercent ?? 0);

    protected string StorageText => string.Format(
        L["AnalyticsStorageUsage"],
        FormatBytes(_summary?.EstimatedStorageBytes ?? 0),
        FormatBytes(_summary?.StorageBudgetBytes ?? 0));

    protected BadgeStyle QuotaBadgeStyle => (_summary?.StorageUsagePercent ?? 0) switch
    {
        >= 95 => BadgeStyle.Danger,
        >= 70 => BadgeStyle.Warning,
        _ => BadgeStyle.Success
    };

    protected ProgressBarStyle QuotaProgressStyle => (_summary?.StorageUsagePercent ?? 0) switch
    {
        >= 95 => ProgressBarStyle.Danger,
        >= 70 => ProgressBarStyle.Warning,
        _ => ProgressBarStyle.Success
    };

    protected override async Task OnParametersSetAsync()
    {
        if (AppId == _lastAppId)
            return;
        _lastAppId = AppId;
        await LoadAsync();
    }

    protected Task RetryAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true;
        _loadFailed = false;
        try
        {
            _summary = await Api.Monitoring.GetAppWebAnalyticsAsync(AppId);
        }
        catch (HttpRequestException)
        {
            _summary = null;
            _loadFailed = true;
        }
        finally
        {
            _loading = false;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB"];
        var value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:N0} {units[unit]}";
    }

    private string FormatDuration(double? durationMs) =>
        durationMs is null ? L["NotAvailable"] : $"{durationMs.Value:N0} ms";
}
