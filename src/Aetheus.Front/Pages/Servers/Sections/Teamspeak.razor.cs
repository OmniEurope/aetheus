// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Components;

namespace Aetheus.Front.Pages.Servers.Sections;

public partial class Teamspeak
{
    [Parameter] public int Id { get; set; }

    [CascadingParameter(Name = "ServerLoader")]
    public Aetheus.Front.Services.ServerDetailLoader? Loader { get; set; }

    private Aetheus.Front.Pages.Servers.ServerDetailSections.ServerTeamspeakSection? _section;
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

    private Task OnTaskCompleted(Aetheus.Shared.DTOs.TaskCompletedNotification notification) =>
        _section?.HandleTaskCompletedAsync(notification) ?? Task.CompletedTask;

    public void Dispose()
    {
        if (_subscribed is not null) _subscribed.OnTaskCompleted -= OnTaskCompleted;
    }
}
