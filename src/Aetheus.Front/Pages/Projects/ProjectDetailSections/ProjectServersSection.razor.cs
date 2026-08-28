// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Projects.ProjectDetailSections;

public partial class ProjectServersSection : ProjectPagedSectionBase<ProjectServerDto>
{
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;

    private List<ProjectServerDto>? _servers;
    private List<ServerDto>? _agentServers;
    private int _totalCount;
    protected override string DefaultSortBy => "DisplayName";

    protected override void ResetForProject() => _agentServers = null;

    protected override async Task LoadAsync()
    {
        var projectId = ProjectId;
        _loading = true;
        try
        {
            var result = await Api.Projects.GetProjectServersPageAsync(
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

    private void OnRowClick(ProjectServerDto server)
    {
        if (server.Type == ProjectServerType.AgentServer && server.ServerId.HasValue)
            Nav.NavigateTo($"/servers/{server.ServerId}/overview");
    }

    private async Task EnsureAgentServersAsync()
    {
        if (_agentServers is not null) return;
        var projectId = ProjectId;
        var servers = await Api.Servers.GetAllServersAsync();
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
            new DialogOptions { Width = "32rem", CloseDialogOnOverlayClick = true, AutoFocusFirstElement = false });

        if (result is not ProjectServerDialogResult data) return;

        var created = await Api.Projects.CreateProjectServerAsync(ProjectId, new CreateProjectServerRequest
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
            new DialogOptions { Width = "32rem", CloseDialogOnOverlayClick = true, AutoFocusFirstElement = false });

        if (result is not ProjectServerDialogResult data) return;

        var updated = await Api.Projects.UpdateProjectServerAsync(ProjectId, server.Id, new UpdateProjectServerRequest
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

        var success = await Api.Projects.DeleteProjectServerAsync(ProjectId, server.Id);
        if (success)
        {
            await LoadAsync();
            Toast.Success("Deleted", "Deleted");
        }
        else
        {
            Toast.Error("Error", "DeleteFailed");
        }
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

}
