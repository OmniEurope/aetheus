// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Layout;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.Constants;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;
using Radzen.Blazor;

namespace Aetheus.Front.Pages.Users;

public partial class Roles : IAsyncDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private AuthStateProvider Auth { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;
    [Inject] private HubConnectionFactory HubFactory { get; set; } = default!;

    private List<RoleDto> _roles = [];
    private RadzenDataGrid<RoleDto>? _grid;
    private int _totalCount;
    private int _page = 1;
    private int _pageSize = 25;
    private string? _sortBy;
    private bool _sortDescending;
    // Must start false so Radzen emits the initial LoadData callback. Starting with IsLoading=true
    // suppresses that callback in Radzen 11 and leaves the grid in a permanent loading state.
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
            new BreadcrumbItem(L["Roles"]));

        // Realtime: refresh the roles list on any create/update/delete/clone/permission change. RT4M.
        _adminRt = new AdminEntitySubscription(HubFactory);
        await _adminRt.StartAsync(AdminEntities.Role, () => InvokeAsync(async () =>
        {
            try { await LoadAsync(); } catch (HttpRequestException) { }
            StateHasChanged();
        }));
    }

    private async Task LoadAsync()
    {
        if (_grid is not null)
        {
            await _grid.Reload();
            return;
        }

        await LoadPageAsync();
    }

    private async Task LoadDataAsync(LoadDataArgs args)
    {
        _pageSize = args.Top ?? 25;
        _page = ((args.Skip ?? 0) / _pageSize) + 1;
        (_sortBy, _sortDescending) = ResolveSort(args);
        await LoadPageAsync();
    }

    private async Task LoadPageAsync()
    {
        _loading = true;
        try
        {
            var result = await Api.GetRoleDtosAsync(
                _page, _pageSize, sortBy: _sortBy, sortDescending: _sortDescending);
            _roles = result.Items;
            _totalCount = result.TotalCount;
        }
        finally
        {
            _loading = false;
        }
    }

    private static (string? SortBy, bool Descending) ResolveSort(LoadDataArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.OrderBy)) return ("Name", false);
        var parts = args.OrderBy.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (parts[0], parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase));
    }

    private void OnRowClick(DataGridRowMouseEventArgs<RoleDto> args)
    {
        if (args.Data is { } role)
            Nav.NavigateTo($"/admin/roles/{role.Id}");
    }

    private async Task OnCreate()
    {
        var created = await Dialog.OpenAsync<RoleCreateDialog>(L["NewRole"],
            new Dictionary<string, object?>(),
            new DialogOptions { Width = "520px", CloseDialogOnOverlayClick = false });

        // Land straight on the new role's page so the admin can set its permission matrix.
        if (created is RoleDto role)
            Nav.NavigateTo($"/admin/roles/{role.Id}");
    }

    private async Task OnDelete(RoleDto role)
    {
        var confirmed = await Dialog.Confirm(
            string.Format(L["ConfirmDeleteRole"], role.Name),
            L["Delete"],
            new ConfirmOptions { OkButtonText = L["Delete"], CancelButtonText = L["Cancel"] });

        if (confirmed != true) return;

        var success = await Api.DeleteRoleAsync(role.Id);
        if (success)
            await LoadAsync();
    }

    private async Task OnClone(RoleDto role)
    {
        var cloned = await Api.CloneRoleAsync(role.Id);
        if (cloned is not null)
        {
            Nav.NavigateTo($"/admin/roles/{cloned.Id}");
        }
    }

    /// <summary>
    /// Localizes well-known seeded role descriptions (F-24). Falls back to the raw value
    /// for user-created roles.
    /// </summary>
    private string LocalizeDescription(RoleDto role) => role.Name switch
    {
        "Admin" => L["RoleAdminDescription"],
        "Reader" => L["RoleReaderDescription"],
        "Contributor" => L["RoleContributorDescription"],
        _ => role.Description ?? string.Empty
    };

    public async ValueTask DisposeAsync()
    {
        if (_adminRt is not null) await _adminRt.DisposeAsync();
    }
}
