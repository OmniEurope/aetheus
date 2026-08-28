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

namespace Aetheus.Front.Pages.PackageFeeds;

public partial class PackageFeedsAdmin : Aetheus.Front.Shared.RealtimeAdminGridPageBase<PackageFeedDto>
{
    private List<PackageFeedDto> _feeds = [];

    protected override async Task OnInitializedAsync()
    {
        if (!await InitializeAdminAsync(AdminEntities.PackageFeed, "PackageFeeds"))
            return;
    }

    private async Task LoadDataAsync(LoadDataArgs args)
    {
        var (page, pageSize) = args.ToPageRequest();
        var (sortBy, sortDescending) = args.ToSortRequest("Name");
        _loading = true;
        try
        {
            var result = await Api.Packages.GetPackageFeedsAsync(
                page, pageSize, _search, sortBy, sortDescending);
            _feeds = result.Items;
            _totalCount = result.TotalCount;
        }
        finally { _loading = false; }
    }

    private async Task OpenCreateAsync()
    {
        var result = await Dialog.OpenAsync<PackageFeedEditDialog>(L["Create"],
            [], new DialogOptions { Width = "620px", CloseDialogOnOverlayClick = true, AutoFocusFirstElement = false });
        if (result is true) await ReloadAsync();
    }

    private async Task ManagePackagesAsync(PackageFeedDto feed)
    {
        await Dialog.OpenAsync<PackageFeedPackagesDialog>(feed.Name,
            new Dictionary<string, object?> { ["FeedId"] = feed.Id },
            new DialogOptions { Width = "760px", CloseDialogOnOverlayClick = true, AutoFocusFirstElement = false });
        await ReloadAsync(); // package count may have changed
    }

    private async Task DeleteAsync(PackageFeedDto feed)
    {
        var confirmed = await Dialog.Confirm(
            string.Format(L["DeletePackageFeedConfirm"], feed.Name), L["Delete"].Value,
            new ConfirmOptions { OkButtonText = L["Delete"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed != true) return;

        await Ui.RunAsync(
            () => Api.Packages.DeletePackageFeedAsync(feed.Id),
            "Deleted",
            ReloadAsync,
            errorKey: "DeleteFailed",
            successTitleKey: "Deleted");
    }

}
