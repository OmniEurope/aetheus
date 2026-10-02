// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Shared;

/// <summary>
/// OE 1.2.0 retired <c>OmniDropDown.Data</c>, <c>TextProperty</c> and <c>ValueProperty</c>: a drop-down now
/// takes its choices as <see cref="OmniOption{TValue}"/>. This builds them from a list with typed selectors
/// instead of the property names the grid read by reflection. The text is the selected value's own text,
/// empty when it is null, as the retired properties gave it.
/// </summary>
public static class OmniOptionExtensions
{
    public static IReadOnlyList<OmniOption<TValue>> ToOptions<TItem, TValue>(
        this IEnumerable<TItem>? items, Func<TItem, TValue> value, Func<TItem, object?> text)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(text);
        return items is null
            ? []
            : items.Select(item => new OmniOption<TValue>(value(item), text(item)?.ToString() ?? string.Empty)).ToArray();
    }
}
