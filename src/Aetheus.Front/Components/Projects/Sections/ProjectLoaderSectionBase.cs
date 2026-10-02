// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Projects.Sections;

public abstract class ProjectLoaderSectionBase : ComponentBase
{
    [Parameter]
    public int Id { get; set; }

    [CascadingParameter(Name = "ProjectLoader")]
    public ProjectDetailLoader? Loader { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        if (Loader is not null) await Loader.EnsureLoadedAsync(Id);
    }
}
