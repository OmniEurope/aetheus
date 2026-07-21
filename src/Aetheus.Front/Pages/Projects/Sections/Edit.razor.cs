// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Services;
using Microsoft.AspNetCore.Components;

namespace Aetheus.Front.Pages.Projects.Sections;

public partial class Edit
{
    [Parameter] public int Id { get; set; }

    // Injected directly (scoped DI) rather than cascaded from ProjectDetailLayout: this page is
    // standalone, so it owns the load itself.
    [Inject] private ProjectDetailLoader Loader { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    protected override async Task OnParametersSetAsync()
    {
        await Loader.EnsureLoadedAsync(Id);
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
