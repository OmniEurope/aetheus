// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;

namespace Aetheus.Front.Components.Servers.ServerDetailSections;

public partial class DockerRelationsGraph : ComponentBase, IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter, EditorRequired] public DockerDataDto Docker { get; set; } = default!;

    private readonly string _svgId = $"docker-graph-{Guid.NewGuid():N}";
    private IJSObjectReference? _module;
    private DotNetObjectReference<DockerRelationsGraph>? _dotNet;
    private bool _initialized;

    private string? _selectedProject;
    private string? _selectedNodeLabel;
    private string? _selectedNodeKind;

    private readonly Dictionary<string, bool> _kindFilter = new()
    {
        ["container"] = true,
        ["image"] = true,
        ["network"] = true,
        ["volume"] = true
    };
    internal IReadOnlyDictionary<string, bool> KindFilter => _kindFilter;

    private (string Key, string Label)[] NodeKinds =>
    [
        ("container", L["Containers"]),
        ("image",     L["DockerImages"]),
        ("network",   L["DockerNetworks"]),
        ("volume",    L["DockerVolumes"])
    ];

    private List<string> _projectOptions = [];

    protected override void OnParametersSet()
    {
        _projectOptions = Docker.Containers.Select(c => c.Project)
            .Concat(Docker.Images.Select(i => i.Project))
            .Concat(Docker.Networks.Select(n => n.Project))
            .Concat(Docker.Volumes.Select(v => v.Project))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _dotNet = DotNetObjectReference.Create(this);
            _module = await JS.InvokeAsync<IJSObjectReference>("import", "./js/dockerGraph.js");
            await _module.InvokeVoidAsync("initDockerGraph", _svgId, _dotNet, BuildData(), $"docker-graph-view-{_svgId}");
            _initialized = true;
        }
    }

    private async Task RebuildAndLoad()
    {
        if (!_initialized || _module is null) return;
        await _module.InvokeVoidAsync("setData", _svgId, BuildData());
    }

    private async Task ResetViewAsync()
    {
        if (!_initialized || _module is null) return;
        await _module.InvokeVoidAsync("resetView", _svgId);
    }

    internal string BuildData()
    {
        bool MatchProject(string? p) => string.IsNullOrEmpty(_selectedProject)
            || string.Equals(p ?? string.Empty, _selectedProject, StringComparison.OrdinalIgnoreCase);

        var nodes = new List<object>();
        var edges = new List<object>();
        AddProjectNodes(nodes, MatchProject);
        if (_kindFilter["container"]) AddContainerNodes(nodes, edges, MatchProject);
        if (_kindFilter["image"]) AddImageNodes(nodes, edges, MatchProject);
        if (_kindFilter["network"]) AddNetworkNodes(nodes, edges, MatchProject);
        if (_kindFilter["volume"]) AddVolumeNodes(nodes, edges, MatchProject);
        return JsonSerializer.Serialize(new { nodes, edges });
    }

    private void AddProjectNodes(ICollection<object> nodes, Func<string?, bool> matchProject)
    {
        var projects = Docker.Containers.Select(c => c.Project)
            .Concat(Docker.Networks.Select(n => n.Project))
            .Concat(Docker.Volumes.Select(v => v.Project))
            .Concat(Docker.Images.Select(i => i.Project))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Where(matchProject)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var p in projects)
            nodes.Add(new { id = $"p:{p}", label = p, group = "project" });
    }

    private void AddContainerNodes(
        ICollection<object> nodes, ICollection<object> edges, Func<string?, bool> matchProject)
    {
        foreach (var c in Docker.Containers.Where(c => matchProject(c.Project)))
        {
            var id = $"c:{c.ContainerId}";
            var group = c.State == "running" ? "container" : "stopped";
            nodes.Add(new { id, label = c.Name, group });
            if (!string.IsNullOrWhiteSpace(c.Project))
                edges.Add(new { from = $"p:{c.Project}", to = id });
            if (_kindFilter["image"] && !string.IsNullOrWhiteSpace(c.Image))
            {
                var imgId = $"i:{c.Image}";
                if (!nodes.Any(n => GetId(n) == imgId))
                    nodes.Add(new { id = imgId, label = c.Image, group = "image" });
                edges.Add(new { from = id, to = imgId });
            }
        }
    }

    private void AddImageNodes(
        ICollection<object> nodes, ICollection<object> edges, Func<string?, bool> matchProject)
    {
        foreach (var img in Docker.Images.Where(i => matchProject(i.Project)))
        {
            var label = string.IsNullOrEmpty(img.Tag) || img.Tag == "<none>"
                ? img.Repository
                : $"{img.Repository}:{img.Tag}";
            var id = $"i:{label}";
            if (!nodes.Any(n => GetId(n) == id))
                nodes.Add(new { id, label, group = "image" });
            if (!string.IsNullOrWhiteSpace(img.Project))
                edges.Add(new { from = $"p:{img.Project}", to = id });
        }
    }

    private void AddNetworkNodes(
        ICollection<object> nodes, ICollection<object> edges, Func<string?, bool> matchProject)
    {
        foreach (var net in Docker.Networks.Where(n => matchProject(n.Project)))
        {
            var id = $"n:{net.NetworkId}";
            nodes.Add(new { id, label = net.Name, group = "network" });
            if (!string.IsNullOrWhiteSpace(net.Project))
                edges.Add(new { from = $"p:{net.Project}", to = id });
        }
    }

    private void AddVolumeNodes(
        ICollection<object> nodes, ICollection<object> edges, Func<string?, bool> matchProject)
    {
        foreach (var vol in Docker.Volumes.Where(v => matchProject(v.Project)))
        {
            var id = $"v:{vol.Name}";
            nodes.Add(new { id, label = vol.Name, group = "volume" });
            if (!string.IsNullOrWhiteSpace(vol.Project))
                edges.Add(new { from = $"p:{vol.Project}", to = id });
        }
    }

    private static string GetId(object obj) =>
        obj.GetType().GetProperty("id")?.GetValue(obj) as string ?? string.Empty;

    [JSInvokable]
    public Task OnGraphNodeClicked(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId)) return Task.CompletedTask;
        var prefix = nodeId[..2];
        var rest = nodeId[2..];
        _selectedNodeKind = prefix switch
        {
            "p:" => L["Project"],
            "c:" => L["Containers"],
            "i:" => L["DockerImages"],
            "n:" => L["DockerNetworks"],
            "v:" => L["DockerVolumes"],
            _ => "?"
        };
        _selectedNodeLabel = prefix == "c:"
            ? Docker.Containers.FirstOrDefault(c => c.ContainerId == rest)?.Name ?? rest
            : prefix == "n:"
                ? Docker.Networks.FirstOrDefault(n => n.NetworkId == rest)?.Name ?? rest
                : rest;
        StateHasChanged();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_module is not null)
            {
                await _module.InvokeVoidAsync("dispose", _svgId);
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException) { /* Circuit disconnected */ }
        catch (Microsoft.JSInterop.JSException) { /* Circuit disconnected */ }
        _dotNet?.Dispose();
    }
}
