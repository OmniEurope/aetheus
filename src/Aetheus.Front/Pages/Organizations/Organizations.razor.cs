// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Layout;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.Constants;
using Aetheus.Shared.DTOs.Organizations;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;
using Radzen.Blazor;

namespace Aetheus.Front.Pages.Organizations;

public partial class Organizations : IAsyncDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private AuthStateProvider Auth { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;
    [Inject] private HubConnectionFactory HubFactory { get; set; } = default!;

    private List<OrganizationDto> _items = [];
    private Aetheus.Front.Shared.AetheusDataGrid<OrganizationDto>? _grid;
    private int _totalCount;
    private string _search = string.Empty;
    private bool _loading;
    // RT4M: shared admin-hub subscription wrapper, owned and disposed by this page.
    private AdminEntitySubscription? _adminRt;

    protected override async Task OnInitializedAsync()
    {
        if (!Auth.IsAdmin)
        {
            Nav.NavigateTo("/");
            return;
        }

        Breadcrumb.Set(
            new BreadcrumbItem(L["Administration"], "/admin"),
            new BreadcrumbItem(L["Organizations"]));

        // Realtime: refresh the list on any org create/update/delete, and on member/project changes
        // (which move the Members/Projects counts shown in the grid). RT4M.
        _adminRt = new AdminEntitySubscription(HubFactory);
        await _adminRt.StartAsync(AdminEntities.Organization, () => InvokeAsync(async () =>
        {
            if (_grid is not null) await _grid.Reload();
            StateHasChanged();
        }));
    }

    private async Task LoadDataAsync(LoadDataArgs args)
    {
        var (page, pageSize) = args.ToPageRequest();
        var (sortBy, sortDescending) = GetSort(args);
        _loading = true;
        try
        {
            var result = await Api.GetOrganizationsAsync(_search, page, pageSize, sortBy, sortDescending);
            _items = result?.Items ?? [];
            _totalCount = result?.TotalCount ?? 0;
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

    private void OnRowClick(DataGridRowMouseEventArgs<OrganizationDto> args)
    {
        if (args.Data is { } org)
            Nav.NavigateTo($"/admin/organizations/{org.Id}");
    }

    private async Task OnCreate()
    {
        // Creation stays a lightweight dialog; on success go straight to the new org's detail page.
        var result = await Dialog.OpenAsync<OrganizationDialog>(L["NewOrganization"],
            new Dictionary<string, object?>(),
            new DialogOptions { Width = "560px", CloseDialogOnOverlayClick = false });
        if (result is OrganizationDto created)
            Nav.NavigateTo($"/admin/organizations/{created.Id}");
    }

    private void OpenDetail(OrganizationDto org)
        => Nav.NavigateTo($"/admin/organizations/{org.Id}");

    private async Task OnDelete(OrganizationDto org)
    {
        var confirmed = await Dialog.Confirm(
            string.Format(L["ConfirmDeleteOrganization"], org.Name),
            L["Delete"],
            new ConfirmOptions { OkButtonText = L["Delete"], CancelButtonText = L["Cancel"] });
        if (confirmed != true) return;
        if (await Api.DeleteOrganizationAsync(org.Id)) await ReloadAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_adminRt is not null) await _adminRt.DisposeAsync();
    }
}
