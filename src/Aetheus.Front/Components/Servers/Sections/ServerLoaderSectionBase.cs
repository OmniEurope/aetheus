// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Servers.Sections;

public abstract class ServerLoaderSectionBase : ComponentBase
{
    [Parameter]
    public int Id { get; set; }

    [CascadingParameter(Name = "ServerLoader")]
    public ServerDetailLoader? Loader { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        if (Loader is not null) await Loader.EnsureLoadedAsync(Id);
    }
}
