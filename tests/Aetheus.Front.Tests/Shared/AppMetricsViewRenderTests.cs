// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Shared;
using Aetheus.Shared.DTOs;

namespace Aetheus.Front.Tests.Shared;

/// <summary>
/// The metrics chart was reworked (unsmoothed line, min/max band, dashed P95, thinned category axis,
/// headline figures) and shipped on a build alone. Rendering it through bUnit is not possible:
/// RadzenChart measures a real viewport in OnAfterRenderAsync and throws a NullReferenceException
/// without one, the same class of limitation the grid Virtualization note in BunitTestHelper records.
/// What the rework actually added is the projection: the band/marker/step decisions and the headline
/// figures. Those are computed in the code-behind and are asserted here, including the single-point
/// series the rework exists to fix, since the previous view rendered nothing below two points.
/// </summary>
public class AppMetricsViewRenderTests
{
    private static MetricSeriesDto Series(int count, bool band, bool p95) => new()
    {
        MetricName = "aspnetcore.memory_pool.allocated",
        Unit = "bytes",
        Points = Enumerable.Range(0, count).Select(i => new MetricPointDto
        {
            Timestamp = new DateTime(2026, 8, 27, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i),
            Value = 10 + i,
            Min = band ? 5 + i : null,
            Max = band ? 20 + i : null,
            P95 = p95 ? 18 + i : null
        }).ToList()
    };

    /// <summary>Runs the real projection the component uses, without rendering Radzen.</summary>
    private static AppMetricsView Project(MetricSeriesDto series, int hours = 24)
    {
        var view = new AppMetricsView();
        Set(view, "_selectedMetric", series.MetricName);
        Set(view, "_hours", hours);
        typeof(AppMetricsView)
            .GetMethod("ApplySeries", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(view, [series]);
        return view;
    }

    private static void Set(AppMetricsView view, string field, object value)
        => typeof(AppMetricsView)
            .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(view, value);

    private static T Get<T>(AppMetricsView view, string field)
        => (T)typeof(AppMetricsView)
            .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(view)!;

    [Fact]
    public void SinglePointSeriesIsPlotted()
    {
        // The regression the rework targets: the old view required two points and showed
        // "not enough data" instead, which is what most live metrics actually look like.
        var view = Project(Series(1, band: false, p95: false));

        Assert.Single(Get<System.Collections.IEnumerable>(view, "_points").Cast<object>());
        Assert.True(Get<bool>(view, "_showMarkers"), "A lone point is invisible without its marker.");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void BandAndP95AreOnlyPlottedWhenEveryPointCarriesThem(bool band, bool p95)
    {
        var view = Project(Series(10, band, p95));

        Assert.Equal(band, Get<bool>(view, "_hasBand"));
        Assert.Equal(p95, Get<bool>(view, "_hasP95"));
    }

    [Fact]
    public void PartialBandIsNotPlotted()
    {
        // Binding a nullable series containing nulls makes Radzen path maths throw, so a band is
        // plotted only when every point has both bounds.
        var series = Series(5, band: true, p95: true);
        var points = series.Points.ToList();
        points[2] = points[2] with { Min = null, P95 = null };

        var view = Project(series with { Points = points });

        Assert.False(Get<bool>(view, "_hasBand"));
        Assert.False(Get<bool>(view, "_hasP95"));
    }

    [Fact]
    public void DenseSeriesDropsMarkersAndThinsTheAxis()
    {
        var view = Project(Series(200, band: true, p95: true));

        Assert.False(Get<bool>(view, "_showMarkers"), "200 markers render as a solid blob.");
        // About ten ticks, whatever the window holds.
        Assert.Equal(20d, Get<double>(view, "_labelStep"));
    }

    [Fact]
    public void HeadlineFiguresSummariseTheSeries()
    {
        var view = Project(Series(3, band: true, p95: false));

        Assert.Equal(11d, Get<double>(view, "_average"));
        // The peak follows the band's upper bound when the series carries one.
        Assert.Equal(22d, Get<double>(view, "_peak"));
    }

    [Fact]
    public void LabelFormatFollowsTheSelectedWindow()
    {
        Assert.All(
            Labels(Project(Series(2, false, false), hours: 24)),
            label => Assert.Matches(@"^\d{2}:\d{2}$", label));
        Assert.All(
            Labels(Project(Series(2, false, false), hours: 168)),
            label => Assert.Matches(@"^\d{2}-\d{2} \d{2}:\d{2}$", label));
    }

    private static IEnumerable<string> Labels(AppMetricsView view)
        => Get<System.Collections.IEnumerable>(view, "_points")
            .Cast<object>()
            .Select(point => (string)point.GetType().GetProperty("Label")!.GetValue(point)!);
}
