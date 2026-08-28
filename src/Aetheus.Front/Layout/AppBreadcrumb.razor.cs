// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Layout;

public partial class AppBreadcrumb : IDisposable
{
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    protected override void OnInitialized()
    {
        Breadcrumb.OnChanged += OnBreadcrumbChanged;
        Breadcrumb.ConfigureFallback(path => BreadcrumbRouteResolver.Resolve(
            Nav.ToBaseRelativePath(path),
            key => L[key]));
    }

    private void OnBreadcrumbChanged() => _ = InvokeAsync(StateHasChanged);

    public void Dispose() => Breadcrumb.OnChanged -= OnBreadcrumbChanged;
}
