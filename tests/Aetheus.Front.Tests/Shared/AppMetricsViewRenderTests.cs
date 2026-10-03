// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;

namespace Aetheus.Front.Tests.Shared;

/// <summary>
/// Verifies the metrics projection independently from the chart renderer: proportional time-axis data,
/// per-point band/P95 decisions and headline figures are computed in the code-behind and asserted here.
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

    /// <summary>Runs the real projection the component uses, without rendering the chart.</summary>
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


    private static int Count(AppMetricsView view, string field)
        => Get<System.Collections.IEnumerable>(view, field).Cast<object>().Count();

    [Fact]
    public void SinglePointSeriesIsPlotted()
    {
        // A lone point is what most live metrics actually look like; it must still be plotted.
        var view = Project(Series(1, band: false, p95: false));

        Assert.Single(Get<System.Collections.IEnumerable>(view, "_points").Cast<object>());
        Assert.True(Get<bool>(view, "_showMarkers"), "A lone point is invisible without its marker.");
    }

    [Theory]
    [InlineData(true, true, 10, 10)]
    [InlineData(false, false, 0, 0)]
    public void BandAndP95PointCountsMatchHowManyPointsCarryThem(
        bool band, bool p95, int expectedBandPoints, int expectedP95Points)
    {
        var view = Project(Series(10, band, p95));

        Assert.Equal(expectedBandPoints, Count(view, "_bandPoints"));
        Assert.Equal(expectedP95Points, Count(view, "_p95Points"));
    }

    [Fact]
    public void PartialBandOnlyDropsThePointsMissingIt()
    {
        // Binding a nullable series containing nulls makes the chart's path maths throw, so the band/P95
        // series are pre-filtered - one point missing the value must not blank the whole band/P95 line,
        // only that one point drops out of it.
        var series = Series(5, band: true, p95: true);
        var points = series.Points.ToList();
        points[2] = points[2] with { Min = null, P95 = null };

        var view = Project(series with { Points = points });

        Assert.Equal(4, Count(view, "_bandPoints"));
        Assert.Equal(4, Count(view, "_p95Points"));
        Assert.Equal(5, Count(view, "_points")); // the raw value line itself is untouched
    }

    [Fact]
    public void DenseSeriesDropsMarkers()
    {
        var view = Project(Series(200, band: true, p95: true));

        Assert.False(Get<bool>(view, "_showMarkers"), "200 markers render as a solid blob.");
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
    public void TimeAxisFormatFollowsTheSelectedWindow()
    {
        // Day before month for a French user (recette 2026-10-02: the axis read "09-03").
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
            var at = new DateTime(2026, 9, 3, 14, 5, 0);
            Assert.Equal("14:05", TimeLabel(Project(Series(2, false, false), hours: 24), at));
            Assert.Equal("03/09 14:05", TimeLabel(Project(Series(2, false, false), hours: 168), at));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    private static string TimeLabel(AppMetricsView view, DateTime value)
        => (string)typeof(AppMetricsView)
            .GetMethod("TimeLabel", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(view, [value])!;

    [Fact]
    public void PointsCarryARealTimestamp_NotAFormattedStringCategory()
    {
        // The x-axis is bound to this property directly, so points must space proportionally to elapsed
        // time instead of being spaced evenly by index like the old formatted-string category did.
        var view = Project(Series(3, false, false));
        var timestamps = Get<System.Collections.IEnumerable>(view, "_points")
            .Cast<object>()
            .Select(point => (DateTime)point.GetType().GetProperty("Timestamp")!.GetValue(point)!)
            .ToList();

        Assert.Equal(3, timestamps.Count);
        Assert.Equal(TimeSpan.FromMinutes(1), timestamps[1] - timestamps[0]);
    }
}
