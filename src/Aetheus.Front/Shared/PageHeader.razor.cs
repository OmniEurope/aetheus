// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Components;
using Radzen;
using Radzen.Blazor;

namespace Aetheus.Front.Shared;

public partial class PageHeader
{
    /// <summary>Header title text.</summary>
    [Parameter, EditorRequired] public string Title { get; set; } = string.Empty;

    /// <summary>Optional Material Symbols icon shown before the title (rendered in the primary color).</summary>
    [Parameter] public string? Icon { get; set; }

    /// <summary>When set, a browser-history back button is shown. The value is retained for call-site compatibility.</summary>
    [Parameter] public string? BackHref { get; set; }

    /// <summary>Title typography - H4 for top-level pages, H5 for in-page section headers.</summary>
    [Parameter] public TextStyle TitleStyle { get; set; } = TextStyle.H4;

    /// <summary>Extra CSS classes on the header wrapper.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Inline content placed right after the title (badges, help link, counters).</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Right-aligned action buttons (pushed past a flex spacer).</summary>
    [Parameter] public RenderFragment? Actions { get; set; }

    /// <summary>Optional second row for search/filter controls.</summary>
    [Parameter] public RenderFragment? Filters { get; set; }

}
