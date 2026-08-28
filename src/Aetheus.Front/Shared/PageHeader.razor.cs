// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Shared;

public partial class PageHeader
{
    /// <summary>Header title text.</summary>
    [Parameter, EditorRequired] public string Title { get; set; } = string.Empty;

    /// <summary>Optional Material Symbols icon shown before the title (rendered in the primary color).</summary>
    [Parameter] public string? Icon { get; set; }

    /// <summary>When set, a smart breadcrumb/history back button is shown. The value is retained for call-site compatibility.</summary>
    [Parameter] public string? BackHref { get; set; }

    /// <summary>Title typography - H4 for top-level pages, H5 for in-page section headers.</summary>
    [Parameter] public TextStyle TitleStyle { get; set; } = TextStyle.H4;

    /// <summary>Optional secondary line displayed below the title row.</summary>
    [Parameter] public string? Subtitle { get; set; }

    /// <summary>Extra CSS classes on the header wrapper.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Inline content placed right after the title (badges, help link, counters).</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Right-aligned action buttons (pushed past a flex spacer).</summary>
    [Parameter] public RenderFragment? Actions { get; set; }

    /// <summary>Optional second row for search/filter controls.</summary>
    [Parameter] public RenderFragment? Filters { get; set; }

    /// <summary>Phone-only expansion state of the badges + actions block. Collapsed by default so a
    /// dense header (status badges plus half a dozen action buttons) does not push the page content
    /// off a phone screen. Ignored above the phone breakpoint, where the CSS never hides the block.</summary>
    private bool _detailsExpanded;

    /// <summary>True when the header carries badges or actions worth collapsing on a phone.</summary>
    private bool HasCollapsibleContent => ChildContent is not null || Actions is not null;

    private string CollapsibleStateClass =>
        _detailsExpanded ? "page-header-collapsible page-header-collapsible-open" : "page-header-collapsible";

    private void ToggleDetails() => _detailsExpanded = !_detailsExpanded;
}
