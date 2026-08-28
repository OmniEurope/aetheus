// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Layout;

public partial class ProjectDetailLayout : IDisposable
{
    protected override void OnInitialized()
    {
        Loader.OnChanged += OnLoaderChanged;
        Nav.LocationChanged += OnLocationChanged;
    }

    private void OnLoaderChanged()
    {
        ReassertBreadcrumb();
        _ = InvokeAsync(StateHasChanged);
    }

    private void OnLocationChanged(object? sender, Microsoft.AspNetCore.Components.Routing.LocationChangedEventArgs e) =>
        ReassertBreadcrumb();

    private void ReassertBreadcrumb()
    {
        if (Loader.Project is null) return;
        var items = BreadcrumbRouteResolver.Resolve(Nav.ToBaseRelativePath(Nav.Uri), key => L[key]).ToArray();
        if (items.Length > 1)
            items[1] = items[1] with { Text = Loader.Project.Name, IsLoading = false };
        Breadcrumb.Set(items);
    }

    public void Dispose()
    {
        Loader.OnChanged -= OnLoaderChanged;
        Nav.LocationChanged -= OnLocationChanged;
    }
}
