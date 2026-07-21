// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Layout;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Front.Shared;
using Aetheus.Shared.Constants;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;
using Radzen.Blazor;

namespace Aetheus.Front.Pages.Users;

public partial class Users : IAsyncDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private AuthStateProvider Auth { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private HubConnectionFactory HubFactory { get; set; } = default!;
    [Inject] private ListCacheService Cache { get; set; } = default!;

    private AetheusDataGrid<UserDto>? _grid;
    private List<UserDto> _users = [];
    private int _totalCount;
    private bool _loading;
    private string? _search;
    private List<string> _availableRoles = [];
    // RT4M: the shared admin-hub subscription wrapper, owned and disposed by this page.
    private AdminEntitySubscription? _adminRt;

    protected override async Task OnInitializedAsync()
    {
        if (!Auth.IsAdmin)
        {
            Nav.NavigateTo("/");
            return;
        }
        Breadcrumb.Set(new BreadcrumbItem(L["Administration"], "/admin"), new BreadcrumbItem(L["Users"]));
        try { _availableRoles = await Api.GetRolesAsync(); }
        catch (HttpRequestException) { _availableRoles = []; }
        // Realtime: reload the grid whenever any session creates/updates/deletes a user (the creator
        // also receives this broadcast, so the list refreshes even within the acting session). RT4M.
        _adminRt = new AdminEntitySubscription(HubFactory);
        await _adminRt.StartAsync(AdminEntities.User,
            () => InvokeAsync(async () => { if (_grid is not null) await _grid.Reload(); }));
        // Stale-while-revalidate: pre-seed the grid from the cached default view (grid PageSize = 50)
        // so revisiting /admin/users paints instantly; OnLoadData then revalidates in the background.
        Cache.Seed<PaginatedResult<UserDto>>(CacheKey(1, 50, null), ApplyUsers);
        // LoadData auto-fired by RadzenDataGrid.
    }

    private string CacheKey(int page, int pageSize, string? search) =>
        $"users:{page}:{pageSize}:{search}";

    private void ApplyUsers(PaginatedResult<UserDto> result)
    {
        _users = result.Items;
        _totalCount = result.TotalCount;
    }

    private async Task OnLoadData(LoadDataArgs args)
    {
        var (page, pageSize) = args.ToPageRequest();
        await Cache.RevalidateAsync(
            CacheKey(page, pageSize, _search),
            () => Api.GetUsersAsync(page, pageSize, _search),
            ApplyUsers,
            loading => _loading = loading,
            () => InvokeAsync(StateHasChanged));
    }

    private async Task ResetAndReload()
    {
        if (_grid is not null) await _grid.GoToPage(0);
    }

    private async Task ClearFilters()
    {
        _search = null;
        await ResetAndReload();
    }

    private async Task OnCreate()
    {
        var created = await Dialog.OpenAsync<UserCreateDialog>(L["NewUser"],
            new Dictionary<string, object?> { ["AvailableRoles"] = _availableRoles },
            new DialogOptions { Width = "560px", CloseDialogOnOverlayClick = false });

        if (created is UserDto)
            await ResetAndReload();
    }

    private async Task OnRolesChanged(UserDto user, object value)
    {
        var roles = (value as IEnumerable<string>)?.ToList() ?? [];
        var request = new UpdateUserRequest
        {
            Username = user.Username,
            Email = user.Email,
            IsActive = user.IsActive,
            // Preserve the current value - UpdateUserRequest.MustChangePassword defaults to false, so
            // omitting it here would silently clear a pending forced-change when only roles changed.
            MustChangePassword = user.MustChangePassword,
            Roles = roles
        };
        await Api.UpdateUserAsync(user.Id, request);
        Toast.Success(L["Saved"], L["RolesUpdated"]);
    }

    private static BadgeStyle GetRoleBadgeStyle(string roleName) =>
        roleName.Equals("Admin", StringComparison.OrdinalIgnoreCase) ? BadgeStyle.Danger
        : roleName.Equals("Contributor", StringComparison.OrdinalIgnoreCase) ? BadgeStyle.Primary
        : roleName.Equals("Reader", StringComparison.OrdinalIgnoreCase) ? BadgeStyle.Info
        : BadgeStyle.Light;

    public async ValueTask DisposeAsync()
    {
        if (_adminRt is not null) await _adminRt.DisposeAsync();
    }
}
