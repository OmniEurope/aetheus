// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Helpers;
using Aetheus.Front.Layout;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.Constants;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.PackageRegistry;

public partial class PackageRegistryAdmin : Aetheus.Front.Shared.RealtimeAdminGridPageBase<PackageRegistryPackageDto>
{
    private List<PackageRegistryPackageDto> _packages = [];

    protected override async Task OnInitializedAsync()
    {
        if (!await InitializeAdminAsync(AdminEntities.PackageRegistry, "PackageRegistry"))
            return;
    }

    private async Task LoadDataAsync(LoadDataArgs args)
    {
        if (!Auth.IsAdmin) return;
        var (page, pageSize) = args.ToPageRequest();
        var (sortBy, sortDescending) = args.ToSortRequest(nameof(PackageRegistryPackageDto.Name));
        _loading = true;
        try
        {
            var result = await Api.Packages.GetRegistryPackagesAsync(page, pageSize, _search,
                sortBy: sortBy, sortDescending: sortDescending);
            _packages = result.Items;
            _totalCount = result.TotalCount;
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task OpenPackageAsync(PackageRegistryPackageDto package)
    {
        await Dialog.OpenAsync<PackageRegistryPackageDialog>(package.Name,
            new Dictionary<string, object?> { ["PackageId"] = package.Id },
            new DialogOptions { Width = "820px", CloseDialogOnOverlayClick = true, AutoFocusFirstElement = false });
        await ReloadAsync();
    }

}
