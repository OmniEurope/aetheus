using System.Globalization;
// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Shared;

public partial class AppMetricsView
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter] public int AppId { get; set; }

    private sealed record ChartPoint(string Label, double Value, double? P95, double? Min, double? Max);
    private sealed record PeriodOption(string Label, int Hours);

    private List<string> _names = [];
    private string? _selectedMetric;
    private int _hours = 24;
    private bool _loading;
    private List<ChartPoint> _points = [];
    private bool _hasP95;
    private bool _hasBand;
    private bool _showMarkers;
    private double _labelStep = 1;
    private double _average;
    private double _peak;
    private string? _unit;
    private int _lastAppId = -1;

    // Y-axis title: the metric's unit when the series carries one (S-DES-YAXU), else the metric name.
    private string AxisTitle => string.IsNullOrWhiteSpace(_unit) ? (_selectedMetric ?? string.Empty) : _unit;

    private readonly List<PeriodOption> _periods =
    [
        new("24h", 24), new("7d", 168), new("90d", 2160)
    ];

    protected override async Task OnParametersSetAsync()
    {
        if (AppId == _lastAppId) return;
        _lastAppId = AppId;
        await LoadNamesAsync();
    }

    private async Task LoadNamesAsync()
    {
        try { _names = await Api.Monitoring.GetAppMetricNamesAsync(AppId); }
        catch (HttpRequestException) { _names = []; }
        _selectedMetric = _names.FirstOrDefault();
        await LoadSeriesAsync();
    }

    private async Task LoadSeriesAsync()
    {
        if (string.IsNullOrEmpty(_selectedMetric))
        {
            _points = [];
            return;
        }

        _loading = true;
        MetricSeriesDto? series = null;
        try { series = await Api.Monitoring.GetAppMetricSeriesAsync(AppId, _selectedMetric, _hours); }
        catch (HttpRequestException) { /* keep last */ }

        ApplySeries(series);
        _loading = false;
    }

    /// <summary>
    /// Turns the fetched series into what the chart binds. Separated from the fetch so the decisions
    /// it makes - which optional series are safe to plot, how dense the axis may get - can be
    /// exercised without a browser: RadzenChart measures a real viewport and cannot be rendered in
    /// bUnit at all.
    /// </summary>
    private void ApplySeries(MetricSeriesDto? series)
    {
        _unit = series?.Unit;
        // A 24h window is read by time of day; longer windows need the date to stay unambiguous.
        var labelFormat = _hours <= 24 ? "HH:mm" : "MM-dd HH:mm";
        _points = series?.Points
            .Select(p => new ChartPoint(
                p.Timestamp.ToString(labelFormat, CultureInfo.InvariantCulture),
                Math.Round(p.Value, 3),
                p.P95.HasValue ? Math.Round(p.P95.Value, 3) : null,
                p.Min.HasValue ? Math.Round(p.Min.Value, 3) : null,
                p.Max.HasValue ? Math.Round(p.Max.Value, 3) : null))
            .ToList() ?? [];
        // Never bind a double? series with nulls (Radzen path math throws) - only plot P95 when all points have it.
        _hasP95 = _points.Count > 0 && _points.All(p => p.P95.HasValue);
        // Same rule as P95: a partially populated band would make Radzen path math throw.
        _hasBand = _points.Count > 0 && _points.All(p => p.Min.HasValue && p.Max.HasValue);
        // Markers help read a sparse series and turn a dense one into a solid blob.
        _showMarkers = _points.Count <= 30;
        // Aim for about ten readable ticks whatever the window holds.
        _labelStep = Math.Max(1, Math.Ceiling(_points.Count / 10.0));
        _average = _points.Count > 0 ? Math.Round(_points.Average(p => p.Value), 3) : 0;
        _peak = _points.Count > 0 ? _points.Max(p => p.Max ?? p.Value) : 0;
    }

    private Task OnFilterChanged() => LoadSeriesAsync();

    // Large counters (bytes, allocations) are unreadable in full; keep small values precise.
    private static string FormatValue(double value) => Math.Abs(value) switch
    {
        >= 1_000_000_000 => (value / 1_000_000_000).ToString("0.##", CultureInfo.InvariantCulture) + "G",
        >= 1_000_000 => (value / 1_000_000).ToString("0.##", CultureInfo.InvariantCulture) + "M",
        >= 10_000 => (value / 1_000).ToString("0.##", CultureInfo.InvariantCulture) + "k",
        _ => value.ToString("0.###", CultureInfo.InvariantCulture)
    };
}
