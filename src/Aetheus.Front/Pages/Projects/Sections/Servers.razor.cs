// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Components;

namespace Aetheus.Front.Pages.Projects.Sections;

public partial class Servers
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
