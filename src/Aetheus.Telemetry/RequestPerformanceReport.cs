// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Telemetry;

/// <summary>What the kept samples say: the slowest calls and the percentiles of every route.</summary>
/// <param name="Since">Oldest sample kept; later than 24 h ago after a restart. Null without samples.</param>
/// <param name="SampleCount">Samples the figures are computed from.</param>
/// <param name="Truncated">True when the cap dropped the oldest samples of the window.</param>
/// <param name="Slowest">The slowest individual requests, slowest first.</param>
/// <param name="Routes">One line per route and method, slowest 95th percentile first.</param>
public sealed record RequestPerformanceSummary(
    DateTime? Since,
    int SampleCount,
    bool Truncated,
    IReadOnlyList<RequestTimingSample> Slowest,
    IReadOnlyList<RouteTimingSummary> Routes);

/// <summary>The timings of one route template and method, in milliseconds.</summary>
public sealed record RouteTimingSummary(
    string Method,
    string Route,
    int Count,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double MaxMs);

/// <summary>Pure: turns kept samples into the slowest calls and per-route percentiles, so the arithmetic
/// is tested without a listener or a clock.</summary>
public static class RequestPerformanceReport
{
    public const int SlowestCount = 20;

    public static RequestPerformanceSummary Build(IReadOnlyList<RequestTimingSample> samples, bool truncated)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var routes = samples
            .GroupBy(sample => (sample.Method, sample.Route))
            .Select(group =>
            {
                var sorted = group.Select(sample => sample.DurationMs).Order().ToArray();
                return new RouteTimingSummary(
                    group.Key.Method,
                    group.Key.Route,
                    sorted.Length,
                    Percentile(sorted, 0.50),
                    Percentile(sorted, 0.95),
                    Percentile(sorted, 0.99),
                    sorted[^1]);
            })
            .OrderByDescending(route => route.P95Ms)
            .ThenBy(route => route.Route, StringComparer.Ordinal)
            .ThenBy(route => route.Method, StringComparer.Ordinal)
            .ToList();

        return new RequestPerformanceSummary(
            samples.Count == 0 ? null : samples.Min(sample => sample.At),
            samples.Count,
            truncated,
            [.. samples.OrderByDescending(sample => sample.DurationMs).Take(SlowestCount)],
            routes);
    }

    /// <summary>Nearest-rank percentile on an ascending array: the smallest value at or above the
    /// requested share of the samples. No interpolation, so every figure is a request that happened.</summary>
    public static double Percentile(double[] ascending, double share)
    {
        ArgumentNullException.ThrowIfNull(ascending);
        if (ascending.Length == 0) return 0;
        var rank = (int)Math.Ceiling(share * ascending.Length);
        return ascending[Math.Clamp(rank - 1, 0, ascending.Length - 1)];
    }
}
