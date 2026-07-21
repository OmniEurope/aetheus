// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.AppMonitoring;

/// <summary>Shared aggregation helpers for the AppMonitoring telemetry rollups.</summary>
internal static class TelemetryMath
{
    public static DateTime TruncateToHour(DateTime t) =>
        new(t.Year, t.Month, t.Day, t.Hour, 0, 0, DateTimeKind.Utc);

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
