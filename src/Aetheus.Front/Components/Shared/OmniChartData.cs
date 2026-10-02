// SPDX-License-Identifier: EUPL-1.2
using OmniEurope.Blazor.Components;

namespace Aetheus.Front.Components.Shared;

internal static class OmniChartData
{
    internal static IReadOnlyList<OmniChartPoint> Indexed<T>(
        IReadOnlyList<T> items,
        Func<T, double> value,
        Func<T, string> label) => items
        .Select((item, index) => new OmniChartPoint(index, value(item), label(item)))
        .ToArray();

    internal static IReadOnlyList<OmniChartPoint> Timed<T>(
        IReadOnlyList<T> items,
        Func<T, DateTime> timestamp,
        Func<T, double> value,
        Func<T, string> label) => items
        .Select(item => new OmniChartPoint(timestamp(item).Ticks, value(item), label(item)))
        .ToArray();

    internal static IReadOnlyList<string> Labels(IReadOnlyList<OmniChartPoint> points) =>
        points.Select(point => point.Label ?? string.Empty).ToArray();
}
