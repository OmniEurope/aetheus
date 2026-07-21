// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Projects.ProjectDetailSections;

public partial class ProjectServerDialog
{
    [Inject] private DialogService DialogService { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter] public ProjectServerType Type { get; set; } = ProjectServerType.ExternalHost;
    [Parameter] public int? ServerId { get; set; }
    [Parameter] public string DisplayName { get; set; } = string.Empty;
    [Parameter] public string Host { get; set; } = string.Empty;
    [Parameter] public int? Port { get; set; }
    [Parameter] public string? Notes { get; set; }
    [Parameter] public bool Saving { get; set; }
    [Parameter] public List<ServerDto>? AgentServers { get; set; }

    /// <summary>When non-null the dialog is in edit mode and type/agent selection is locked.</summary>
    [Parameter] public ProjectServerDto? Editing { get; set; }

    private bool _editMode => Editing is not null;
    private static readonly ProjectServerType[] _serverTypes = Enum.GetValues<ProjectServerType>();

    private void OnTypeChanged(ProjectServerType type)
    {
        Type = type;
        if (type == ProjectServerType.ExternalHost)
            ServerId = null;
    }

    private void OnAgentServerSelected(int? serverId)
    {
        ServerId = serverId;
        if (serverId is not null && AgentServers is not null)
        {
            var server = AgentServers.FirstOrDefault(s => s.Id == serverId.Value);
            if (server is not null)
            {
                Host = server.Hostname;
                if (string.IsNullOrWhiteSpace(DisplayName))
                    DisplayName = server.Name;
            }
        }
    }

    private void OnSubmit()
    {
        DialogService.Close(new ProjectServerDialogResult
        {
            Type = Type,
            ServerId = ServerId,
            DisplayName = DisplayName,
            Host = Host,
            Port = Port,
            Notes = Notes
        });
    }

    private void OnCancel() => DialogService.Close(null);
}

public sealed class ProjectServerDialogResult
{
    public ProjectServerType Type { get; init; }
    public int? ServerId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public int? Port { get; init; }
    public string? Notes { get; init; }
}
