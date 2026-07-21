// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Front.Shared;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Projects.ProjectDetailSections;

public partial class ProjectServersSection
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;

    [Parameter, EditorRequired] public int ProjectId { get; set; }

    private List<ProjectServerDto>? _servers;
    private int? _loadedProjectId;
    private List<ServerDto>? _agentServers;
    private AetheusDataGrid<ProjectServerDto>? _grid;
    private int _totalCount;
    private int _page = 1;
    private int _pageSize = 25;
    private string _sortBy = "DisplayName";
    private bool _sortDescending;
    private string? _search;
    private bool _loading;

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedProjectId == ProjectId) return;
        _loadedProjectId = ProjectId;
        _page = 1;
        _agentServers = null;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var projectId = ProjectId;
        _loading = true;
        try
        {
            var result = await Api.GetProjectServersPageAsync(
                projectId, _page, _pageSize, _search, _sortBy, _sortDescending);
            if (ProjectId != projectId) return;
            _servers = result.Items;
            _totalCount = result.TotalCount;
        }
        catch (HttpRequestException)
        {
            if (ProjectId != projectId) return;
            _servers = [];
            _totalCount = 0;
        }
        finally { if (ProjectId == projectId) _loading = false; }
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

    private void OnRowClick(ProjectServerDto server)
    {
        if (server.Type == ProjectServerType.AgentServer && server.ServerId.HasValue)
            Nav.NavigateTo($"/servers/{server.ServerId}/overview");
    }

    private async Task EnsureAgentServersAsync()
    {
        if (_agentServers is not null) return;
        var projectId = ProjectId;
        var servers = await Api.GetAllServersAsync();
        if (ProjectId == projectId) _agentServers = servers;
    }

    private async Task OpenAddDialogAsync()
    {
        await EnsureAgentServersAsync();
        var result = await Dialog.OpenAsync<ProjectServerDialog>(
            L["AddProjectServer"],
            new Dictionary<string, object?>
            {
                { "AgentServers", _agentServers }
            },
            new DialogOptions { Width = "32rem", CloseDialogOnOverlayClick = true });

        if (result is not ProjectServerDialogResult data) return;

        var created = await Api.CreateProjectServerAsync(ProjectId, new CreateProjectServerRequest
        {
            Type = data.Type,
            ServerId = data.Type == ProjectServerType.AgentServer ? data.ServerId : null,
            DisplayName = data.DisplayName,
            Host = data.Host,
            Port = data.Port,
            Notes = data.Notes
        });
        if (created is not null)
        {
            await LoadAsync();
            Toast.Success("Created", "ProjectServerCreated");
        }
    }

    private async Task OpenEditDialogAsync(ProjectServerDto server)
    {
        var result = await Dialog.OpenAsync<ProjectServerDialog>(
            L["EditProjectServer"],
            new Dictionary<string, object?>
            {
                { "Editing", server },
                { "DisplayName", server.DisplayName },
                { "Host", server.Host },
                { "Port", server.Port },
                { "Notes", server.Notes },
                { "Type", server.Type }
            },
            new DialogOptions { Width = "32rem", CloseDialogOnOverlayClick = true });

        if (result is not ProjectServerDialogResult data) return;

        var updated = await Api.UpdateProjectServerAsync(ProjectId, server.Id, new UpdateProjectServerRequest
        {
            DisplayName = data.DisplayName,
            Host = data.Host,
            Port = data.Port,
            Notes = data.Notes
        });
        if (updated is not null)
        {
            await LoadAsync();
            Toast.Success("Updated", "ProjectServerUpdated");
        }
    }

    private async Task DeleteServerAsync(ProjectServerDto server)
    {
        var confirmed = await Dialog.Confirm(L["DeleteConfirm"].Value, L["Delete"].Value,
            new ConfirmOptions { OkButtonText = L["Delete"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed != true) return;

        var success = await Api.DeleteProjectServerAsync(ProjectId, server.Id);
        if (success) await LoadAsync();
    }

    private static BadgeStyle GetTypeBadge(ProjectServerType type) => type switch
    {
        ProjectServerType.AgentServer => BadgeStyle.Info,
        ProjectServerType.ExternalHost => BadgeStyle.Warning,
        _ => BadgeStyle.Light
    };

    private static BadgeStyle GetStatusBadge(ServerStatus? status) => status switch
    {
        ServerStatus.Online => BadgeStyle.Success,
        ServerStatus.Offline => BadgeStyle.Danger,
        _ => BadgeStyle.Light
    };

    private static (string SortBy, bool Descending) GetSort(LoadDataArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.OrderBy)) return ("DisplayName", false);
        var parts = args.OrderBy.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (parts[0], parts.Length > 1
            && string.Equals(parts[1], "desc", StringComparison.OrdinalIgnoreCase));
    }
}
