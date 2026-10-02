// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Shared;

public partial class AppVisitorsView
{
    [Inject] protected ApiClient Api { get; set; } = default!;
    [Inject] protected IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter] public int AppId { get; set; }

    protected sealed record ChartPoint(string Label, int UniqueVisitors);

    protected int _today;
    protected bool _loading = true;
    protected bool _loadFailed;
    protected List<ChartPoint> _points = [];
    protected IReadOnlyList<OmniChartPoint> VisitorPoints => OmniChartData.Indexed(_points, point => point.UniqueVisitors, point => point.Label);
    private int _lastAppId = -1;

    protected override async Task OnParametersSetAsync()
    {
        if (AppId == _lastAppId) return;
        _lastAppId = AppId;
        await LoadAsync();
    }

    protected async Task RetryAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        _loadFailed = false;
        try
        {
            var series = await Api.Monitoring.GetAppVisitorSeriesAsync(AppId);
            _today = series?.Today ?? 0;
            _points = series?.Points
                .Select(point => new ChartPoint(point.DayUtc.ToString("MM-dd"), point.UniqueVisitors))
                .ToList() ?? [];
        }
        catch (HttpRequestException)
        {
            _loadFailed = true;
        }
        finally
        {
            _loading = false;
        }
    }
}
