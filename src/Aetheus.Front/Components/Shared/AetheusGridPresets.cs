// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Shared;

/// <summary>
/// PLAN-012: the settings every Aetheus table shares, registered once as the default OmniEurope preset of
/// <see cref="OmniDataGrid{TItem}"/>. The raw grids no longer repeat them, so a grid cannot silently miss
/// one (recette R-086). A grid still writes a setting explicitly to differ.
/// Virtualization stays per grid: it needs a bounded height, and <see cref="AetheusGrid.Virtualization"/>
/// is a switch read at render, not a value to freeze at startup.
/// </summary>
public static class AetheusGridPresets
{
    public const string Default = "aetheus";

    /// <summary>Idempotent, like AddOmniEuropeBlazor: a host (or a test) that wires twice registers once.</summary>
    public static IServiceCollection AddAetheusGridPresets(this IServiceCollection services) =>
        services.Any(d => d.ServiceType == typeof(OmniPresetRegistry))
            ? services
            : services.AddOmniEuropePreset(typeof(OmniDataGrid<>), Default, new Dictionary<string, object?>
            {
                [nameof(OmniDataGrid<object>.AllowSorting)] = true,
                [nameof(OmniDataGrid<object>.Filterable)] = true,
                [nameof(OmniDataGrid<object>.FilterMode)] = OmniDataGridFilterMode.Simple,
                [nameof(OmniDataGrid<object>.ShowHeaderFilterMenu)] = true,
                [nameof(OmniDataGrid<object>.AllowColumnResize)] = true,
                // OE made the double click on a column edge (fit to content) opt-in; Aetheus tables had it.
                [nameof(OmniDataGrid<object>.AllowColumnAutoFit)] = true,
                [nameof(OmniDataGrid<object>.AllowAlternatingRows)] = true,
                [nameof(OmniDataGrid<object>.HighlightRowOnHover)] = true,
                [nameof(OmniDataGrid<object>.Density)] = OmniDensity.Compact,
                // STD-GRIDTITLE (recette R-538): a column title stays on one line and is cut with an
                // ellipsis when it does not fit; OE wraps titles by default.
                [nameof(OmniDataGrid<object>.HeaderWrap)] = OmniDataGridHeaderWrap.Truncate,
            }, isDefault: true);
}
