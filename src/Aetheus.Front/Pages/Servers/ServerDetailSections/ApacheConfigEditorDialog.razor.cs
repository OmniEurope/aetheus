// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

public partial class ApacheConfigEditorDialog : IDisposable
{
    [Parameter] public ApacheConfigEditorModel Model { get; set; } = new();

    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    private string _content = string.Empty;
    private bool _saving;

    protected override void OnInitialized()
    {
        _content = Model.Content;
        Model.OnChanged += HandleModelChanged;
    }

    // The config text arrives asynchronously over SignalR (via the section's HandleTaskCompleted,
    // which updates the shared model and raises OnChanged). Pull the latest content into the
    // editable buffer - but don't clobber edits the user already started typing.
    private void HandleModelChanged()
    {
        if (string.IsNullOrEmpty(_content))
            _content = Model.Content;
        InvokeAsync(StateHasChanged);
    }

    private void Cancel() => Dialog.Close(null);

    private void Save()
    {
        _saving = true;
        Dialog.Close(new ApacheVHostSaveRequest
        {
            SiteName = Model.SiteName,
            Content = _content
        });
    }

    public void Dispose() => Model.OnChanged -= HandleModelChanged;
}
