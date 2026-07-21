// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace Aetheus.Front.Layout;

public sealed class BreadcrumbService : IDisposable
{
    private readonly NavigationManager _nav;
    private readonly List<BreadcrumbItem> _items = [];
    private string _lastPath;

    public IReadOnlyList<BreadcrumbItem> Items => _items;
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

    public void Clear()
    {
        _items.Clear();
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
        _items.Clear();
        OnChanged?.Invoke();
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

public sealed record BreadcrumbItem(string Text, string? Href = null);
