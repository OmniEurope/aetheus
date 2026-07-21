// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Layout;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Front.Shared;
using Aetheus.Shared.Constants;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Plugins;

public partial class PluginManagement : IAsyncDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private AuthStateProvider Auth { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;
    [Inject] private HubConnectionFactory HubFactory { get; set; } = default!;

    private List<PluginRegistrationDto> _plugins = [];
    private AetheusDataGrid<PluginRegistrationDto>? _grid;
    private int _totalCount;
    private int _currentPage = 1;
    private int _pageSize = 25;
    private string _sortBy = "Name";
    private bool _sortDescending;
    private string? _search;
    private bool _loading = true;
    private bool _authorized;
    // RT4M: shared admin-hub subscription wrapper, owned and disposed by this page.
    private AdminEntitySubscription? _adminRt;

    protected override async Task OnInitializedAsync()
    {
        if (!Auth.IsAdmin)
        {
            Navigation.NavigateTo("/");
            return;
        }

        _authorized = true;
        Breadcrumb.Set(new BreadcrumbItem(L["Plugins"]));
        await LoadPageAsync();
        // Realtime: refresh the list when plugins are registered/updated/unregistered (e.g. an agent
        // registering a plugin), so the management view reflects changes without a manual reload. RT4M.
        _adminRt = new AdminEntitySubscription(HubFactory);
        await _adminRt.StartAsync(AdminEntities.Plugin, () => InvokeAsync(async () =>
        {
            await LoadPageAsync();
            StateHasChanged();
        }));
    }

    private async Task LoadPageAsync()
    {
        _loading = true;
        try
        {
            var result = await Api.GetPluginsPageAsync(
                _currentPage, _pageSize, _search, _sortBy, _sortDescending);
            _plugins = result.Items;
            _totalCount = result.TotalCount;
        }
        catch (HttpRequestException)
        {
            _plugins = [];
            _totalCount = 0;
        }
        finally { _loading = false; }
    }

    private async Task OnLoadDataAsync(LoadDataArgs args)
    {
        (_currentPage, _pageSize) = args.ToPageRequest();
        (_sortBy, _sortDescending) = GetSort(args);
        await LoadPageAsync();
    }

    private async Task OnSearchChangedAsync(object _)
    {
        if (_grid is not null && _currentPage != 1)
        {
            _currentPage = 1;
            await _grid.GoToPage(0);
            return;
        }

        _currentPage = 1;
        await LoadPageAsync();
    }

    // X4D8: the register form now lives in PluginRegisterDialog. Reload the list when it reports success.
    private async Task OpenRegisterDialog()
    {
        var result = await Dialog.OpenAsync<PluginRegisterDialog>(
            L["Register"], new Dictionary<string, object?>(), PluginRegisterDialog.DialogOptions());
        if (result is true) await LoadPageAsync();
    }

    private async Task TogglePlugin(PluginRegistrationDto plugin)
    {
        var newStatus = plugin.Status == PluginStatus.Enabled ? PluginStatus.Disabled : PluginStatus.Enabled;
        var result = await Api.UpdatePluginAsync(plugin.Id, new UpdatePluginRequest
        {
            Description = plugin.Description,
            Status = newStatus,
            EntryPoint = plugin.EntryPoint,
            ConfigurationJson = plugin.ConfigurationJson
        });
        if (result is not null)
        {
            await LoadPageAsync();
        }
    }

    private async Task UnregisterPlugin(int id)
    {
        var confirmed = await Dialog.Confirm(L["DeleteConfirm"].Value, L["Unregister"].Value,
            new ConfirmOptions { OkButtonText = L["Unregister"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed != true) return;

        var success = await Api.UnregisterPluginAsync(id);
        if (success)
        {
            await LoadPageAsync();
            Toast.Success("Deleted", "PluginUnregistered");
        }
        else
        {
            Toast.Error("Error", "DeleteFailed");
        }
    }

    private static BadgeStyle GetStatusBadgeStyle(PluginStatus status) => status switch
    {
        PluginStatus.Enabled => BadgeStyle.Success,
        PluginStatus.Disabled => BadgeStyle.Warning,
        PluginStatus.Error => BadgeStyle.Danger,
        _ => BadgeStyle.Info
    };

    private static (string SortBy, bool Descending) GetSort(LoadDataArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.OrderBy)) return ("Name", false);
        var parts = args.OrderBy.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (parts[0], parts.Length > 1
            && string.Equals(parts[1], "desc", StringComparison.OrdinalIgnoreCase));
    }

    public async ValueTask DisposeAsync()
    {
        if (_adminRt is not null) await _adminRt.DisposeAsync();
    }
}
