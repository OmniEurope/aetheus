// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Components;

namespace Aetheus.Front.Pages.Servers.Sections;

public partial class Docker
{
    [Parameter] public int Id { get; set; }

    [CascadingParameter(Name = "ServerLoader")]
    public Aetheus.Front.Services.ServerDetailLoader? Loader { get; set; }

    private Aetheus.Front.Pages.Servers.ServerDetailSections.ServerDockerSection? _section;
    private Aetheus.Front.Services.ServerDetailLoader? _subscribed;

    protected override async Task OnParametersSetAsync()
    {
        if (Loader is not null) await Loader.EnsureLoadedAsync(Id);
        if (Loader is not null && Loader != _subscribed)
        {
            if (_subscribed is not null) _subscribed.OnTaskCompleted -= OnTaskCompleted;
            Loader.OnTaskCompleted += OnTaskCompleted;
            _subscribed = Loader;
        }
    }

    private Task OnTaskCompleted(Aetheus.Shared.DTOs.TaskCompletedNotification n)
    {
        _section?.HandleTaskCompleted(n);
        return Task.CompletedTask;
    }

    // ServerChanged on the section asks the parent to merge a fresh Server snapshot. The
    // loader is the single source of truth - heartbeats already refresh through Loader.OnChanged.
    private void OnServerChanged(Aetheus.Shared.DTOs.ServerDetailDto _) => StateHasChanged();

    public void Dispose()
    {
        if (_subscribed is not null) _subscribed.OnTaskCompleted -= OnTaskCompleted;
    }
}
