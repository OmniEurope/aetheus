// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;

namespace Aetheus.Front.Shared;

public partial class OwnerTypeSelector
{
    [Parameter] public int? ProjectId { get; set; }
    [Parameter] public EventCallback<int?> ProjectIdChanged { get; set; }

    [Parameter] public int? EnvironmentId { get; set; }
    [Parameter] public EventCallback<int?> EnvironmentIdChanged { get; set; }

    [Parameter] public int? ProjectServerId { get; set; }
    [Parameter] public EventCallback<int?> ProjectServerIdChanged { get; set; }

    /// <summary>Pre-loaded project list passed by the parent form.</summary>
    [Parameter, EditorRequired] public List<ProjectDto> Projects { get; set; } = [];

    private OwnerKind _kind = OwnerKind.Global;
    private readonly string _radioGroupName = $"owner-kind-{Guid.NewGuid():N}";
    private bool _kindInitialized;
    private int? _selectedProjectId;
    private List<EnvironmentDto> _environments = [];
    private List<ProjectServerDto> _projectServers = [];

    protected override async Task OnParametersSetAsync()
    {
        // Bound owner ids define the initial mode. Afterwards the user's mode choice
        // must survive the parent re-render caused by SetOwnership(null, null, null):
        // re-inferring on every render immediately snapped Project/Environment/Server
        // back to Global before the user could choose the scoped owner.
        if (_kindInitialized) return;
        _kindInitialized = true;

        // Infer the initial kind from the bound values
        if (ProjectServerId is > 0)
        {
            _kind = OwnerKind.ProjectServer;
            // Find the project that owns this ProjectServer
            _selectedProjectId = await FindProjectForServerAsync(ProjectServerId.Value);
        }
        else if (EnvironmentId is > 0)
        {
            _kind = OwnerKind.Environment;
            _selectedProjectId = await FindProjectForEnvironmentAsync(EnvironmentId.Value);
        }
        else if (ProjectId is > 0)
        {
            _kind = OwnerKind.Project;
            _selectedProjectId = ProjectId;
        }
        else
        {
            _kind = OwnerKind.Global;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && _selectedProjectId is > 0)
        {
            if (_kind == OwnerKind.Environment)
                await LoadEnvironmentsAsync(_selectedProjectId.Value);
            else if (_kind == OwnerKind.ProjectServer)
                await LoadProjectServersAsync(_selectedProjectId.Value);
            StateHasChanged();
        }
    }

    private async Task OnKindChanged(OwnerKind newKind)
    {
        _kind = newKind;
        _selectedProjectId = null;
        _environments = [];
        _projectServers = [];
        await SetOwnership(null, null, null);
    }

    private async Task OnProjectChanged(object value)
    {
        _selectedProjectId = (int?)value;
        _environments = [];
        _projectServers = [];

        if (_kind == OwnerKind.Project)
        {
            await SetOwnership(_selectedProjectId, null, null);
        }
        else if (_kind == OwnerKind.Environment && _selectedProjectId is > 0)
        {
            await LoadEnvironmentsAsync(_selectedProjectId.Value);
            await SetOwnership(null, null, null);
        }
        else if (_kind == OwnerKind.ProjectServer && _selectedProjectId is > 0)
        {
            await LoadProjectServersAsync(_selectedProjectId.Value);
            await SetOwnership(null, null, null);
        }
    }

    private async Task OnEnvironmentChanged(object value)
    {
        await SetOwnership(null, (int?)value, null);
    }

    private async Task OnProjectServerChanged(object value)
    {
        await SetOwnership(null, null, (int?)value);
    }

    private async Task SetOwnership(int? projectId, int? environmentId, int? projectServerId)
    {
        await ProjectIdChanged.InvokeAsync(projectId);
        await EnvironmentIdChanged.InvokeAsync(environmentId);
        await ProjectServerIdChanged.InvokeAsync(projectServerId);
    }

    private async Task LoadEnvironmentsAsync(int projectId)
    {
        _environments = await Api.GetAllEnvironmentsAsync(projectId);
    }

    private async Task LoadProjectServersAsync(int projectId)
    {
        _projectServers = await Api.GetProjectServersAsync(projectId);
    }

    private async Task<int?> FindProjectForServerAsync(int projectServerId)
    {
        var lookups = Projects.Select(async project =>
        {
            try
            {
                var servers = await Api.GetProjectServersAsync(project.Id);
                return servers.Any(server => server.Id == projectServerId) ? project.Id : (int?)null;
            }
            catch (HttpRequestException)
            {
                return null;
            }
        });
        return (await Task.WhenAll(lookups)).FirstOrDefault(projectId => projectId is not null);
    }

    private async Task<int?> FindProjectForEnvironmentAsync(int environmentId)
    {
        try
        {
            return (await Api.GetEnvironmentAsync(environmentId))?.ProjectId;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public enum OwnerKind { Global, Project, Environment, ProjectServer }
}
