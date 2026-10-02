// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.AppMonitoring;

/// <summary>Shared aggregation helpers for the AppMonitoring telemetry rollups.</summary>
internal static class TelemetryMath
{
    public static DateTime TruncateToHour(DateTime t) =>
        new(t.Year, t.Month, t.Day, t.Hour, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Turns a time-ordered run of raw <see cref="AppMetricSample"/> values for one logical series into the
    /// values that are actually meaningful to plot: unchanged for <see cref="MetricKind.Gauge"/>/
    /// <see cref="MetricKind.Histogram"/> (already instantaneous), or the delta between consecutive points
    /// for <see cref="MetricKind.Sum"/> (a monotonic cumulative counter). The first point of a
    /// <see cref="MetricKind.Sum"/> run has no predecessor and is dropped; a negative delta means the
    /// process restarted and the counter reset, so that interval is dropped too rather than shown as a
    /// misleading dip below zero.
    /// </summary>
    public static List<(DateTime Timestamp, double Value)> ToDisplayValues(
        IReadOnlyList<(DateTime Timestamp, double Value)> orderedCumulative, MetricKind kind)
    {
        if (kind != MetricKind.Sum)
            return orderedCumulative.ToList();

        var result = new List<(DateTime Timestamp, double Value)>(Math.Max(0, orderedCumulative.Count - 1));
        for (var i = 1; i < orderedCumulative.Count; i++)
        {
            var delta = orderedCumulative[i].Value - orderedCumulative[i - 1].Value;
            if (delta < 0)
                continue; // counter reset (process restart) - not a real negative measurement
            result.Add((orderedCumulative[i].Timestamp, delta));
        }
        return result;
    }

    /// <summary>Linear-interpolation percentile over an unsorted list (sorts a copy).</summary>
    public static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count == 0) return 0;
        var sorted = values.ToArray();
        Array.Sort(sorted);
        if (sorted.Length == 1) return sorted[0];
        var rank = percentile * (sorted.Length - 1);
        var low = (int)Math.Floor(rank);
        var high = (int)Math.Ceiling(rank);
        if (low == high) return sorted[low];
        var weight = rank - low;
        return sorted[low] * (1 - weight) + sorted[high] * weight;
    }
}
