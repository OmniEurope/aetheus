// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Helpers;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.Constants;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.PackageRegistry;

public partial class PackageRegistryPackageDialog : ComponentBase, IAsyncDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private HubConnectionFactory HubFactory { get; set; } = default!;

    [Parameter] public int PackageId { get; set; }

    private PackageRegistryPackageDetailDto? _package;
    private bool _loading = true;
    private AdminEntitySubscription? _adminRt;
    private readonly TrailingReloadCoalescer _realtimeReload = new(500);

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
        _adminRt = new AdminEntitySubscription(HubFactory);
        await _adminRt.StartAsync(
            AdminEntities.PackageRegistry,
            () => InvokeAsync(() => _realtimeReload.RequestAsync(LoadAsync)));
    }

    private async Task LoadAsync()
    {
        _loading = true;
        try
        {
            _package = await Api.Packages.GetRegistryPackageAsync(PackageId);
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task ToggleListedAsync(PackageRegistryVersionDto version)
    {
        var status = await Api.Packages.UpdateRegistryVersionAsync(PackageId, version.Id, !version.IsListed);
        if (!status.Success)
        {
            Toast.Error("Error", "SaveFailed");
            return;
        }
        await LoadAsync();
        Toast.Success("Saved", "Saved");
    }

    private void Close() => Dialog.Close();

    public async ValueTask DisposeAsync()
    {
        if (_adminRt is not null) await _adminRt.DisposeAsync();
    }
}
