// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Components;

namespace Aetheus.Front.Pages.Projects.Sections;

// 0-a: the releases grid now lives in the shared ReleasesList (self-loading, project-scoped). The
// loader is still used to resolve the project (page title / layout chrome); list data + sync are
// owned by ReleasesList.
public partial class Releases
{
    [Parameter] public int Id { get; set; }

    [CascadingParameter(Name = "ProjectLoader")]
    public Aetheus.Front.Services.ProjectDetailLoader? Loader { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        if (Loader is not null)
            await Loader.EnsureLoadedAsync(Id);
    }
}
