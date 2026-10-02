// SPDX-License-Identifier: EUPL-1.2
using Microsoft.Extensions.Logging;

namespace Aetheus.Front.Components.Shared;

/// <summary>
/// Recette R-120 / R-215: the two views of the pipelines list, Catalogue and Runs, switched from the page
/// header. The URL (<c>?tab=catalog|runs</c>) wins; without one, the view this user last opened comes
/// back from the browser. Storage is a convenience: when the browser refuses it the list opens on the
/// catalogue.
/// </summary>
internal sealed class PipelineListViews(IJSRuntime js, ILogger logger, Func<string> storageKey)
{
    public const string CatalogSlug = "catalog";
    public const string RunsSlug = "runs";
    public const int CatalogIndex = 0;
    public static readonly IReadOnlyList<string> Slugs = [CatalogSlug, RunsSlug];

    private string? _lastTabParameter;
    private bool _tabParameterSeen;

    public int Index { get; private set; }

    public bool IsCatalog => Index == CatalogIndex;

    public string CurrentSlug => Slugs[Math.Clamp(Index, 0, Slugs.Count - 1)];

    public static int IndexOf(string? slug) =>
        Slugs.ToList().FindIndex(known => string.Equals(known, slug, StringComparison.OrdinalIgnoreCase));

    public static OmniIconName? Icon(string slug) => slug == RunsSlug ? OmniIconName.Reset : OmniIconName.Rows;

    /// <summary>The last view this user opened, used only as the initial selection.</summary>
    public async Task RestoreAsync()
    {
        try
        {
            var index = IndexOf(await js.InvokeAsync<string?>("localStorage.getItem", storageKey()));
            if (index > 0)
                Index = index;
        }
        catch (JSException exception)
        {
            logger.LogDebug(exception, "Pipelines tab preference could not be read");
        }
    }

    /// <summary>Back and forward re-apply the view the URL names; a bare re-render leaves it alone.
    /// Without a known slug the first pass keeps the remembered view, a later clean URL goes back to
    /// the catalogue. A slug of an enclosing tab set (the hub's <c>?tab=used</c>) names no view here.</summary>
    public void ApplyTabParameter(string? tab)
    {
        if (_tabParameterSeen && string.Equals(tab, _lastTabParameter, StringComparison.Ordinal)) return;
        var index = IndexOf(tab);
        if (index >= 0)
            Index = index;
        else if (_tabParameterSeen && string.IsNullOrEmpty(tab))
            Index = CatalogIndex;
        _lastTabParameter = tab;
        _tabParameterSeen = true;
    }

    /// <summary>A view pick pushes a history entry (the first view keeps a clean URL), so back and
    /// forward move between the views, and is remembered for this user's next visit.</summary>
    public async Task SelectAsync(string slug, NavigationManager navigation)
    {
        var index = IndexOf(slug);
        if (index < 0 || index == Index) return;
        Index = index;
        _lastTabParameter = index == CatalogIndex ? null : slug;
        navigation.NavigateTo(navigation.GetUriWithQueryParameter("tab", _lastTabParameter));
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", storageKey(), CurrentSlug);
        }
        catch (JSException exception)
        {
            logger.LogDebug(exception, "Pipelines tab preference could not be saved");
        }
    }
}
