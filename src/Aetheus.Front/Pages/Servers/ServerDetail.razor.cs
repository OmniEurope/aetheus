// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Aetheus.Front.Pages.Servers;

/// <summary>
/// Thin redirector for the legacy bare <c>/servers/{id}</c> route (and its old
/// <c>?section=</c> query string). The monolithic detail page was retired in favour of the
/// routed Sections pattern (<c>/servers/{id}/&lt;section&gt;</c> + <see cref="Aetheus.Front.Layout.ServerDetailLayout"/>).
/// This component just forwards to the matching section route, defaulting to <c>overview</c>.
/// </summary>
public partial class ServerDetail : ComponentBase
{
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter] public int Id { get; set; }
    [SupplyParameterFromQuery(Name = "section")] public string? Section { get; set; }

    /// <summary>Known section slugs (the <c>ServerSection.*</c> constants) - anything else falls
    /// back to <c>overview</c> so we never redirect to a non-existent route.</summary>
    private static readonly HashSet<string> KnownSections = typeof(ServerSection)
        .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
        .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    protected override void OnInitialized()
    {
        var section = !string.IsNullOrEmpty(Section) && KnownSections.Contains(Section)
            ? Section
            : ServerSection.Overview;
        Nav.NavigateTo($"/servers/{Id}/{section}", replace: true);
    }
}
