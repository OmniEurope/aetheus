// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Components;

namespace Aetheus.Front.Pages.Servers.Sections;

public partial class Overview
{
    [Parameter] public int Id { get; set; }

    /// <summary>
    /// Cascaded from the layout. Section pages consume the loader to read the server detail
    /// + capability flags without duplicating the fetch.
    /// </summary>
    [CascadingParameter(Name = "ServerLoader")]
    public Aetheus.Front.Services.ServerDetailLoader? Loader { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        // Idempotent - calls for the same Id no-op inside the loader (semaphore + id check).
        if (Loader is not null)
            await Loader.EnsureLoadedAsync(Id);
    }
}
