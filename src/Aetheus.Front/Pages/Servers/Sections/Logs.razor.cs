// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Components;

namespace Aetheus.Front.Pages.Servers.Sections;

public partial class Logs
{
    [Parameter] public int Id { get; set; }

    [CascadingParameter(Name = "ServerLoader")]
    public Aetheus.Front.Services.ServerDetailLoader? Loader { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        if (Loader is not null) await Loader.EnsureLoadedAsync(Id);
    }
}
