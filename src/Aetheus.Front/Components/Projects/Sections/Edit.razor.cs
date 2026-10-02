// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Projects.Sections;

public partial class Edit
{
    [Parameter] public int Id { get; set; }

    // Injected directly (scoped DI) rather than cascaded from ProjectDetailLayout: this page is
    // standalone, so it owns the load itself.
    [Inject] private ProjectDetailLoader Loader { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private Aetheus.Front.Layout.BreadcrumbService Breadcrumb { get; set; } = default!;

    protected override async Task OnParametersSetAsync()
    {
        await Loader.EnsureLoadedAsync(Id);
        // Recette R-187: this page has no ProjectDetailLayout, which is what swaps the route trail's
        // loading segment for the project name; without it the trail stayed on "Loading...".
        if (Loader.Project is { } project)
            Breadcrumb.Set(
                new Aetheus.Front.Layout.BreadcrumbItem(L["Projects"], "/projects"),
                new Aetheus.Front.Layout.BreadcrumbItem(project.Name, $"/projects/{Id}/overview"),
                new Aetheus.Front.Layout.BreadcrumbItem(L["Edit"]));
    }

    // ProjectEditSection raises OnSaved after a successful update. Force-reload the shared (scoped)
    // loader BEFORE navigating so the overview header/sections reflect the edit - a plain navigate
    // would hit EnsureLoadedAsync's same-id no-op and render the stale pre-edit project.
    private async Task OnSaved()
    {
        await Loader.ForceReloadAsync(Id);
        Nav.NavigateTo($"/projects/{Id}/overview");
    }
}
