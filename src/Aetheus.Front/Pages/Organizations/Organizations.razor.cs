// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs.Organizations;

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
    [Inject] private NotifyHelper Notify { get; set; } = default!;

    private List<OrganizationDto> _items = [];
    private Aetheus.Front.Shared.AetheusDataGrid<OrganizationDto>? _grid;
    private int _totalCount;
    private string _search = string.Empty;
    private bool _loading;
    private int _silentRefreshDepth;
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
            await RefreshSilentlyAsync();
            StateHasChanged();
        }));
    }

    private async Task LoadDataAsync(LoadDataArgs args)
    {
        var (page, pageSize) = args.ToPageRequest();
        var (sortBy, sortDescending) = args.ToSortRequest("Name");
        var showLoading = _silentRefreshDepth == 0;
        if (showLoading) _loading = true;
        try
        {
            var result = await Api.Servers.GetOrganizationsAsync(_search, page, pageSize, sortBy, sortDescending);
            _items = result?.Items ?? [];
            _totalCount = result?.TotalCount ?? 0;
        }
        finally
        {
            if (showLoading) _loading = false;
        }
    }

    private Task ReloadAsync() => _grid?.Reload() ?? Task.CompletedTask;

    private async Task RefreshSilentlyAsync()
    {
        _silentRefreshDepth++;
        try
        {
            await ReloadAsync();
        }
        finally
        {
            _silentRefreshDepth--;
        }
    }

    private Task ResetSearchAsync() => _grid?.GoToPage(0) ?? Task.CompletedTask;

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
            new DialogOptions { Width = "560px", CloseDialogOnOverlayClick = false, AutoFocusFirstElement = false });
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
        if (await Api.Servers.DeleteOrganizationAsync(org.Id))
        {
            await RefreshSilentlyAsync();
            Notify.Success("Deleted", "Deleted");
        }
        else
        {
            Notify.Error("Error", "DeleteFailed");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_adminRt is not null) await _adminRt.DisposeAsync();
    }
}
