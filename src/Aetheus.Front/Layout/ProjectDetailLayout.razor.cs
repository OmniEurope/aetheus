// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Layout;

public partial class ProjectDetailLayout : IDisposable
{
    /// <summary>Recette R-431: the section's own entries of the header "..." menu.</summary>
    private readonly ProjectSectionMenu _sectionMenu = new();

    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;

    /// <summary>Recette R-431: the follow state of the project, loaded with the project rather than by
    /// the menu entry at each opening of the menu.</summary>
    private Aetheus.Front.Components.Notifications.ProjectSubscriptionState _subscription = default!;

    protected override void OnInitialized()
    {
        _subscription = new(Api, Toast);
        _sectionMenu.Changed += OnSectionMenuChanged;
        Loader.OnChanged += OnLoaderChanged;
        Nav.LocationChanged += OnLocationChanged;
        // PLAN-003 lot 1: the project name now lives ONLY in the trail, so it has to be set on mount
        // too - a loader that already holds the project fires no change event to trigger it later.
        ReassertBreadcrumb();
        LoadSubscription();
    }

    private void OnLoaderChanged()
    {
        ReassertBreadcrumb();
        LoadSubscription();
        _ = InvokeAsync(StateHasChanged);
    }

    private void LoadSubscription()
    {
        if (Loader.Project is not { } project) return;
        _ = InvokeAsync(async () =>
        {
            if (await _subscription.LoadAsync(project.Id)) StateHasChanged();
        });
    }

    private void OnSectionMenuChanged() => _ = InvokeAsync(StateHasChanged);

    private void OnLocationChanged(object? sender, Microsoft.AspNetCore.Components.Routing.LocationChangedEventArgs e)
    {
        ReassertBreadcrumb();
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>Recette R-219: every project section shows its own icon before the title.</summary>
    private string? SectionIcon
    {
        get
        {
            var segments = Nav.ToBaseRelativePath(Nav.Uri).Split('?', '#')[0].Split('/', StringSplitOptions.RemoveEmptyEntries);
            return ProjectNavigationCatalog.IconFor(segments.Length > 2 ? segments[2] : "overview");
        }
    }

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
        _sectionMenu.Changed -= OnSectionMenuChanged;
        Loader.OnChanged -= OnLoaderChanged;
        Nav.LocationChanged -= OnLocationChanged;
    }
}
