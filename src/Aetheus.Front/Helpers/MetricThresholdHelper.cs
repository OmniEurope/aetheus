// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Helpers;

/// <summary>
/// Chantier H: maps a resource-usage percentage to a semantic severity, so CPU/RAM/disk bars turn
/// amber as they get tight and red when they are near-saturated, instead of all reading the same neutral color.
/// </summary>
internal static class MetricThresholdHelper
{
    private const double WarnThreshold = 75d;
    private const double DangerThreshold = 90d;

    /// <summary>Percentage of <paramref name="used"/> over <paramref name="total"/>, clamped to 0 when total is 0.</summary>
    internal static double Percent(double used, double total) => total > 0 ? used / total * 100d : 0d;

    /// <summary>CSS modifier class for a progress bar fill, by threshold (&gt;90 danger, &gt;75 warn, else ok).</summary>
    internal static string BarClass(double percent) =>
        percent > DangerThreshold ? "metric-bar-danger"
        : percent > WarnThreshold ? "metric-bar-warn"
        : "metric-bar-ok";
}
