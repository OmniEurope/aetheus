// SPDX-License-Identifier: EUPL-1.2

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
    private bool _userChoseKind;
    private int? _selectedProjectId;
    private List<EnvironmentDto> _environments = [];
    private List<ProjectServerDto> _projectServers = [];

    protected override async Task OnParametersSetAsync()
    {
        // Bound owner ids define the mode until the user picks one. Afterwards the user's choice
        // must survive the parent re-render caused by SetOwnership(null, null, null): re-inferring
        // on every render immediately snapped Project/Environment/Server back to Global before the
        // user could choose the scoped owner.
        //
        // What this must NOT do is latch on the first parameter set regardless of content. Owners
        // arrive late: PipelineEdit assigns _model.ProjectId inside an async load that completes
        // after the first render, so latching on render one inferred Global from a still-null id and
        // never looked again - opening /pipelines/new?projectId=13 from a project offered "Global"
        // with no project dropdown. Latching on the user's choice instead of on the first render
        // keeps the protection above and lets a late-arriving owner still be honoured.
        if (_userChoseKind) return;

        // Infer the kind from the bound values
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

    // Not gated on firstRender: the owner the mode was inferred from can arrive after it, and its
    // dropdown would then render permanently empty. Gated on the list actually being missing
    // instead, which is idempotent across the re-renders that follow.
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_selectedProjectId is not > 0) return;

        if (_kind == OwnerKind.Environment && _environments.Count == 0)
            await LoadEnvironmentsAsync(_selectedProjectId.Value);
        else if (_kind == OwnerKind.ProjectServer && _projectServers.Count == 0)
            await LoadProjectServersAsync(_selectedProjectId.Value);
        else
            return;

        StateHasChanged();
    }

    private async Task OnKindChanged(OwnerKind newKind)
    {
        // From here the user owns the mode: never re-infer it from the bound ids, which
        // SetOwnership below is about to clear.
        _userChoseKind = true;
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
        _environments = await Api.Servers.GetAllEnvironmentsAsync(projectId);
    }

    private async Task LoadProjectServersAsync(int projectId)
    {
        _projectServers = await Api.Projects.GetProjectServersAsync(projectId);
    }

    private async Task<int?> FindProjectForServerAsync(int projectServerId)
    {
        var lookups = Projects.Select(async project =>
        {
            try
            {
                var servers = await Api.Projects.GetProjectServersAsync(project.Id);
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
            return (await Api.Servers.GetEnvironmentAsync(environmentId))?.ProjectId;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public enum OwnerKind { Global, Project, Environment, ProjectServer }
}
