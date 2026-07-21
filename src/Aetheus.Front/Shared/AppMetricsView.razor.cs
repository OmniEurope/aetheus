// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Aetheus.Front.Shared;

public partial class AppMetricsView
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter] public int AppId { get; set; }

    private sealed record ChartPoint(string Label, double Value, double? P95);
    private sealed record PeriodOption(string Label, int Hours);

    private List<string> _names = [];
    private string? _selectedMetric;
    private int _hours = 24;
    private bool _loading;
    private List<ChartPoint> _points = [];
    private bool _hasP95;
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
        try { _names = await Api.GetAppMetricNamesAsync(AppId); }
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
        try { series = await Api.GetAppMetricSeriesAsync(AppId, _selectedMetric, _hours); }
        catch (HttpRequestException) { /* keep last */ }

        _unit = series?.Unit;
        _points = series?.Points
            .Select(p => new ChartPoint(
                p.Timestamp.ToString("MM-dd HH:mm"),
                Math.Round(p.Value, 3),
                p.P95.HasValue ? Math.Round(p.P95.Value, 3) : null))
            .ToList() ?? [];
        // Never bind a double? series with nulls (Radzen path math throws) - only plot P95 when all points have it.
        _hasP95 = _points.Count > 0 && _points.All(p => p.P95.HasValue);
        _loading = false;
    }

    private Task OnFilterChanged() => LoadSeriesAsync();
}
