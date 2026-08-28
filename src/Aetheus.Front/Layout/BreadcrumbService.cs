// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Components.Routing;

namespace Aetheus.Front.Layout;

/// <summary>
/// Stateful breadcrumb for the currently rendered route. Routed pages may replace the route-derived
/// fallback with entity names once their data is loaded. Query-only navigation deliberately preserves
/// the current items. A path change immediately installs a fallback for the destination, so the global
/// breadcrumb slot never becomes empty while an asynchronous page is loading.
/// </summary>
public sealed class BreadcrumbService : IDisposable
{
    private readonly NavigationManager _nav;
    private readonly List<BreadcrumbItem> _items = [];
    private string _lastPath;
    private Func<string, IReadOnlyList<BreadcrumbItem>>? _fallbackFactory;

    public IReadOnlyList<BreadcrumbItem> Items => _items;

    /// <summary>
    /// Nearest clickable ancestor of the current breadcrumb item. The final item is always treated as
    /// the current page, even if a caller accidentally assigns it an href.
    /// </summary>
    public string? ParentHref => _items
        .Take(Math.Max(0, _items.Count - 1))
        .LastOrDefault(item => !string.IsNullOrWhiteSpace(item.Href))
        ?.Href;

    public event Action? OnChanged;

    public BreadcrumbService(NavigationManager nav)
    {
        _nav = nav;
        _lastPath = PathOf(nav.Uri);
        _nav.LocationChanged += OnLocationChanged;
    }

    public void Set(params BreadcrumbItem[] items)
    {
        _items.Clear();
        _items.AddRange(items);
        OnChanged?.Invoke();
    }

    public void ConfigureFallback(Func<string, IReadOnlyList<BreadcrumbItem>> fallbackFactory)
    {
        _fallbackFactory = fallbackFactory;
        if (_items.Count == 0)
            SetFallback(_lastPath);
    }

    public void Clear()
    {
        SetFallback(_lastPath);
    }

    private void SetFallback(string path)
    {
        _items.Clear();
        if (_fallbackFactory is not null)
            _items.AddRange(_fallbackFactory(path));
        OnChanged?.Invoke();
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        // Only reset on a real page (path) change. A query-only navigation - a tab switch via
        // UrlSyncedTabs (?tab=), a filter/pagination sync (?projectId=), a replace: true URL tidy -
        // keeps the same logical page, so the breadcrumb (set once in OnInitialized/OnParametersSet
        // behind an early-return guard) must survive. Clearing on every LocationChanged stranded the
        // breadcrumb empty after the first tab click, because the owning page never re-Set it.
        var newPath = PathOf(e.Location);
        if (string.Equals(newPath, _lastPath, StringComparison.OrdinalIgnoreCase))
            return;

        _lastPath = newPath;
        SetFallback(newPath);
    }

    private static string PathOf(string uri)
    {
        var parsed = new Uri(uri, UriKind.RelativeOrAbsolute);
        var path = parsed.IsAbsoluteUri
            ? parsed.GetLeftPart(UriPartial.Path)
            : uri.Split('?', '#')[0];
        // Normalize a trailing slash so "/settings" and "/settings/" are the same logical page (a stray
        // slash from a link/redirect must not read as a page change and wipe the breadcrumb). Keep a
        // lone "/" (root) intact.
        return path.Length > 1 ? path.TrimEnd('/') : path;
    }

    public void Dispose()
    {
        _nav.LocationChanged -= OnLocationChanged;
    }
}

public sealed record BreadcrumbItem(string Text, string? Href = null, bool IsLoading = false);
