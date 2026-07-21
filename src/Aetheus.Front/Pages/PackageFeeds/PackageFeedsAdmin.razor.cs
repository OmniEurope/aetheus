// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Layout;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.PackageFeeds;

public partial class PackageFeedsAdmin : ComponentBase
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private AuthStateProvider Auth { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;
    [Inject] private UiActions Ui { get; set; } = default!;

    private List<PackageFeedDto> _feeds = [];
    private Aetheus.Front.Shared.AetheusDataGrid<PackageFeedDto>? _grid;
    private int _totalCount;
    private string _search = string.Empty;
    private bool _loading;

    protected override Task OnInitializedAsync()
    {
        if (!Auth.IsAdmin)
        {
            Nav.NavigateTo("/");
            return Task.CompletedTask;
        }
        Breadcrumb.Set(new BreadcrumbItem(L["Administration"], "/admin"), new BreadcrumbItem(L["PackageFeeds"]));
        return Task.CompletedTask;
    }

    private async Task LoadDataAsync(LoadDataArgs args)
    {
        var (page, pageSize) = args.ToPageRequest();
        var (sortBy, sortDescending) = GetSort(args);
        _loading = true;
        try
        {
            var result = await Api.GetPackageFeedsAsync(
                page, pageSize, _search, sortBy, sortDescending);
            _feeds = result.Items;
            _totalCount = result.TotalCount;
        }
        finally { _loading = false; }
    }

    private Task ReloadAsync() => _grid?.Reload() ?? Task.CompletedTask;

    private Task ResetSearchAsync() => _grid?.GoToPage(0) ?? Task.CompletedTask;

    private static (string SortBy, bool Descending) GetSort(LoadDataArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.OrderBy)) return ("Name", false);
        var parts = args.OrderBy.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (parts[0], parts.Length > 1
            && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase));
    }

    private async Task OpenCreateAsync()
    {
        var result = await Dialog.OpenAsync<PackageFeedEditDialog>(L["Create"],
            [], new DialogOptions { Width = "620px", CloseDialogOnOverlayClick = true });
        if (result is true) await ReloadAsync();
    }

    private async Task ManagePackagesAsync(PackageFeedDto feed)
    {
        await Dialog.OpenAsync<PackageFeedPackagesDialog>(feed.Name,
            new Dictionary<string, object?> { ["FeedId"] = feed.Id },
            new DialogOptions { Width = "760px", CloseDialogOnOverlayClick = true });
        await ReloadAsync(); // package count may have changed
    }

    private async Task DeleteAsync(PackageFeedDto feed)
    {
        var confirmed = await Dialog.Confirm(
            string.Format(L["DeletePackageFeedConfirm"], feed.Name), L["Delete"].Value,
            new ConfirmOptions { OkButtonText = L["Delete"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed != true) return;

        await Ui.RunAsync(
            () => Api.DeletePackageFeedAsync(feed.Id),
            "Deleted",
            ReloadAsync,
            errorKey: "DeleteFailed",
            successTitleKey: "Deleted");
    }
}
