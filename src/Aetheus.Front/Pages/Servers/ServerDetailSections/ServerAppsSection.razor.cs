// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Helpers;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Front.Shared;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

public partial class ServerAppsSection
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;

    [Parameter, EditorRequired] public int ServerId { get; set; }

    private List<ServerAppDto>? _apps;
    private int? _loadedServerId;
    private AetheusDataGrid<ServerAppDto>? _grid;
    private int _totalCount;
    private int _page = 1;
    private int _pageSize = 25;
    private string _sortBy = "Name";
    private bool _sortDescending;
    private string? _search;
    private bool _loading;
    private bool _addVisible;
    private bool _addSaving;
    private string _addName = string.Empty;
    private string _addVersion = string.Empty;
    private int? _addPort;
    private string _addSource = "manual";
    private static readonly string[] _sources = ["manual", "systemd", "docker"];

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedServerId == ServerId) return;
        _loadedServerId = ServerId;
        _page = 1;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var serverId = ServerId;
        _loading = true;
        try
        {
            var result = await Api.GetServerAppsPageAsync(
                serverId, _page, _pageSize, _search, _sortBy, _sortDescending);
            if (ServerId != serverId) return;
            _apps = result.Items;
            _totalCount = result.TotalCount;
        }
        catch (HttpRequestException)
        {
            if (ServerId != serverId) return;
            _apps = [];
            _totalCount = 0;
        }
        finally { if (ServerId == serverId) _loading = false; }
    }

    private async Task OnLoadDataAsync(LoadDataArgs args)
    {
        (_page, _pageSize) = args.ToPageRequest();
        (_sortBy, _sortDescending) = GetSort(args);
        await LoadAsync();
    }

    private async Task OnSearchChangedAsync(object _)
    {
        if (_grid is not null && _page != 1)
        {
            _page = 1;
            await _grid.GoToPage(0);
            return;
        }
        _page = 1;
        await LoadAsync();
    }

    private async Task AddAppAsync()
    {
        _addSaving = true;
        var result = await Api.CreateServerAppAsync(ServerId, new CreateServerAppRequest
        {
            Name = _addName,
            Version = string.IsNullOrWhiteSpace(_addVersion) ? null : _addVersion,
            Port = _addPort,
            Source = _addSource
        });
        if (result is not null)
        {
            await LoadAsync();
            _addVisible = false;
            _addName = string.Empty;
            _addVersion = string.Empty;
            _addPort = null;
            Toast.Success("Created", "ApplicationCreated");
        }
        _addSaving = false;
    }

    private async Task DeleteAppAsync(ServerAppDto app)
    {
        var confirmed = await Dialog.Confirm(L["DeleteConfirm"].Value, L["Delete"].Value,
            new ConfirmOptions { OkButtonText = L["Delete"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed != true) return;

        var success = await Api.DeleteServerAppAsync(ServerId, app.Id);
        if (success)
        {
            await LoadAsync();
        }
    }

    private static BadgeStyle GetAppStatusBadge(ServerAppStatus status) => status switch
    {
        ServerAppStatus.Running => BadgeStyle.Success,
        ServerAppStatus.Stopped => BadgeStyle.Light,
        ServerAppStatus.Error => BadgeStyle.Danger,
        _ => BadgeStyle.Warning
    };

    private static (string SortBy, bool Descending) GetSort(LoadDataArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.OrderBy)) return ("Name", false);
        var parts = args.OrderBy.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (parts[0], parts.Length > 1
            && string.Equals(parts[1], "desc", StringComparison.OrdinalIgnoreCase));
    }
}
