// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Shared;

public abstract class RealtimeAdminGridPageBase<T> : ComponentBase, IAsyncDisposable
    where T : notnull
{
    [Inject] protected ApiClient Api { get; set; } = default!;
    [Inject] protected NavigationManager Nav { get; set; } = default!;
    [Inject] protected AuthStateProvider Auth { get; set; } = default!;
    [Inject] protected IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] protected DialogService Dialog { get; set; } = default!;
    [Inject] protected BreadcrumbService Breadcrumb { get; set; } = default!;
    [Inject] protected HubConnectionFactory HubFactory { get; set; } = default!;
    [Inject] protected UiActions Ui { get; set; } = default!;

    protected AetheusDataGrid<T>? _grid;
    protected int _totalCount;
    protected string _search = string.Empty;
    protected bool _loading;

    private AdminEntitySubscription? _adminSubscription;
    private readonly TrailingReloadCoalescer _realtimeReload = new(500);

    protected async Task<bool> InitializeAdminAsync(string entity, string titleKey)
    {
        if (!Auth.IsAdmin)
        {
            Nav.NavigateTo("/");
            return false;
        }

        Breadcrumb.Set(
            new BreadcrumbItem(L["Administration"], "/admin"),
            new BreadcrumbItem(L[titleKey]));
        _adminSubscription = new AdminEntitySubscription(HubFactory);
        await _adminSubscription.StartAsync(
            entity,
            () => InvokeAsync(() => _realtimeReload.RequestAsync(ReloadAsync))).ConfigureAwait(false);
        return true;
    }

    protected Task ReloadAsync() => _grid?.Reload() ?? Task.CompletedTask;

    protected Task ResetSearchAsync() => _grid?.GoToPage(0) ?? Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_adminSubscription is not null)
            await _adminSubscription.DisposeAsync().ConfigureAwait(false);
    }
}
