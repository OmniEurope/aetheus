// SPDX-License-Identifier: EUPL-1.2

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
    private bool _loadFailed;
    private int _silentRefreshDepth;
    private string? _search;
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
        // Realtime: reload the grid whenever any session creates/updates/deletes a user (the creator
        // also receives this broadcast, so the list refreshes even within the acting session). RT4M.
        _adminRt = new AdminEntitySubscription(HubFactory);
        await _adminRt.StartAsync(AdminEntities.User,
            () => InvokeAsync(async () =>
            {
                Cache.InvalidatePrefix("users:");
                await RefreshSilentlyAsync(() => _grid?.Reload() ?? Task.CompletedTask);
            }));
        // Stale-while-revalidate: pre-seed the grid from the cached default view (grid PageSize = 50)
        // so revisiting /admin/users paints instantly; OnLoadData then revalidates in the background.
        Cache.Seed<PaginatedResult<UserDto>>(CacheKey(1, 50, null), ApplyUsers);
        // LoadData auto-fired by RadzenDataGrid.
    }

    // The sort is part of the key: without it two different orders share one cache entry and the
    // second one is served the first one's rows, which looks exactly like a sort that does nothing.
    private string CacheKey(int page, int pageSize, string? search, string? sortBy = null, bool sortDescending = false) =>
        $"users:{page}:{pageSize}:{search}:{sortBy}:{sortDescending}";

    private void ApplyUsers(PaginatedResult<UserDto> result)
    {
        _users = result.Items;
        _totalCount = result.TotalCount;
    }

    private async Task OnLoadData(LoadDataArgs args)
    {
        _loadFailed = false;
        var (page, pageSize) = args.ToPageRequest();
        var (sortBy, sortDescending) = args.ToSortRequest(nameof(UserDto.Username));
        var showLoading = _silentRefreshDepth == 0;
        await Cache.RevalidateAsync(
            CacheKey(page, pageSize, _search, sortBy, sortDescending),
            () => Api.Auth.GetUsersAsync(page, pageSize, _search, sortBy, sortDescending),
            ApplyUsers,
            loading =>
            {
                if (showLoading || !loading) _loading = loading;
            },
            () => InvokeAsync(StateHasChanged),
            _ => _loadFailed = true);
    }

    private async Task RetryLoadAsync()
    {
        if (_grid is not null) await _grid.Reload();
    }

    private async Task ResetAndReload()
    {
        if (_grid is not null) await _grid.GoToPage(0);
    }

    private async Task RefreshSilentlyAsync(Func<Task> refresh)
    {
        _silentRefreshDepth++;
        try
        {
            await refresh();
        }
        finally
        {
            _silentRefreshDepth--;
        }
    }

    private async Task ClearFilters()
    {
        _search = null;
        await ResetAndReload();
    }

    private async Task OnCreate()
    {
        var created = await Dialog.OpenAsync<UserCreateDialog>(L["NewUser"],
            new Dictionary<string, object?>(),
            new DialogOptions { Width = "560px", CloseDialogOnOverlayClick = false, AutoFocusFirstElement = false });

        if (created is UserDto)
            await RefreshSilentlyAsync(ResetAndReload);
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
        var updated = await Api.Auth.UpdateUserAsync(user.Id, request);
        if (updated is not null)
        {
            Toast.Success(L["Saved"], L["RolesUpdated"]);
        }
        else
        {
            Toast.Error(L["Error"], L["SaveFailed"]);
            await RefreshSilentlyAsync(() => _grid?.Reload() ?? Task.CompletedTask);
        }
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
