// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using Radzen;

namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

public partial class ServerDockerSection : IAsyncDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private ClipboardService Clipboard { get; set; } = default!;
    [Inject] private TooltipService TooltipService { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter, EditorRequired] public ServerDetailDto Server { get; set; } = default!;
    [Parameter, EditorRequired] public int ServerId { get; set; }
    [Parameter] public bool DockerInitialLoaded { get; set; }
    [Parameter] public EventCallback<ServerDetailDto> ServerChanged { get; set; }

    private bool _dockerRefreshing;
    private string? _dockerActionTarget;
    private bool _dockerAutoRefresh;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private CancellationTokenSource? _autoRefreshCts;
    private Task? _autoRefreshTask;
    private int? _activeServerId;
    private bool _disposed;

    private string? _logsContainerId;
    private string? _logsContent;

    private string _pullImageName = string.Empty;
    private bool _imagePulling;

    private string _containerSearch = string.Empty;
    private string _imageSearch = string.Empty;
    private string _composeSearch = string.Empty;
    private string _networkSearch = string.Empty;
    private string _volumeSearch = string.Empty;

    private bool _pruning;


    private string? _inspectContainerId;
    private string? _inspectContent;

    private bool _composeEditorVisible;
    private string _composeEditorStack = string.Empty;
    private string _composeEditorContent = string.Empty;
    private bool _composeDeploying;
    private bool _composeFileLoading;

    private string? _shellContainerId;
    private string _shellContainerName = string.Empty;
    private string _shellCommand = string.Empty;
    private string _shellOutput = string.Empty;

    private bool _dockerInitialLoaded;

    private string? _envContainerId;
    private string _envContainerName = string.Empty;
    private string? _envContent;

    private string? _browseContainerId;
    private string _browseContainerName = string.Empty;
    private string _browsePath = "/";
    private string? _browseContent;

    private string _buildImageTag = string.Empty;
    private string _buildDockerfileContent = string.Empty;
    private bool _buildingImage;

    private IList<GroupDescriptor> _containerGroups =
    [
        new GroupDescriptor { Property = nameof(DockerContainerDto.Project), Title = "Project" }
    ];

    private IList<GroupDescriptor> _imageGroups =
    [
        new GroupDescriptor { Property = nameof(DockerImageDto.Project), Title = "Project" }
    ];

    private IList<GroupDescriptor> _networkGroups =
    [
        new GroupDescriptor { Property = nameof(DockerNetworkDto.Project), Title = "Project" }
    ];

    private IList<GroupDescriptor> _volumeGroups =
    [
        new GroupDescriptor { Property = nameof(DockerVolumeDto.Project), Title = "Project" }
    ];

    // Start all groups collapsed by default. Two-way bound so individual group toggling
    // still works after the initial render - Radzen sets the state via the @bind callback.
    private bool? _allContainerGroupsExpanded = false;
    private bool? _allImageGroupsExpanded = false;
    private bool? _allNetworkGroupsExpanded = false;
    private bool? _allVolumeGroupsExpanded = false;

    private List<DockerContainerDto> FilteredContainers => Server.Docker.Containers
        .Where(c => string.IsNullOrWhiteSpace(_containerSearch)
            || c.Name.Contains(_containerSearch, StringComparison.OrdinalIgnoreCase)
            || c.Image.Contains(_containerSearch, StringComparison.OrdinalIgnoreCase)
            || c.State.Contains(_containerSearch, StringComparison.OrdinalIgnoreCase))
        .ToList();

    private List<DockerImageDto> FilteredImages => Server.Docker.Images
        .Where(i => string.IsNullOrWhiteSpace(_imageSearch)
            || i.Repository.Contains(_imageSearch, StringComparison.OrdinalIgnoreCase)
            || i.Tag.Contains(_imageSearch, StringComparison.OrdinalIgnoreCase))
        .ToList();

    private List<DockerComposeStackDto> FilteredCompose => Server.Docker.ComposeStacks
        .Where(s => string.IsNullOrWhiteSpace(_composeSearch)
            || s.Name.Contains(_composeSearch, StringComparison.OrdinalIgnoreCase)
            || s.Status.Contains(_composeSearch, StringComparison.OrdinalIgnoreCase))
        .ToList();

    private List<DockerNetworkDto> FilteredNetworks => Server.Docker.Networks
        .Where(n => string.IsNullOrWhiteSpace(_networkSearch)
            || n.Name.Contains(_networkSearch, StringComparison.OrdinalIgnoreCase)
            || n.Driver.Contains(_networkSearch, StringComparison.OrdinalIgnoreCase))
        .ToList();

    private List<DockerVolumeDto> FilteredVolumes => Server.Docker.Volumes
        .Where(v => string.IsNullOrWhiteSpace(_volumeSearch)
            || v.Name.Contains(_volumeSearch, StringComparison.OrdinalIgnoreCase)
            || v.Driver.Contains(_volumeSearch, StringComparison.OrdinalIgnoreCase))
        .ToList();

    private List<string> DockerResourceNames => Server.Docker.Containers.Select(c => c.Name).ToList();

    protected override void OnParametersSet()
    {
        if (_activeServerId != ServerId)
        {
            _activeServerId = ServerId;
            StopAutoRefresh();
            _dockerAutoRefresh = false;
            ResetServerState();
            _dockerInitialLoaded = DockerInitialLoaded;
        }
        else if (DockerInitialLoaded)
            _dockerInitialLoaded = true;
    }

    public void HandleTaskCompleted(TaskCompletedNotification notification)
    {
        if (notification.ServerId != ServerId) return;
        if (notification.Output is not null)
            DispatchTaskOutput(notification.TaskName, notification.Output);

        // HTC5: HandleTaskCompleted is invoked from the SignalR callback (outside the render loop),
        // so marshal the re-render back onto the renderer's sync context - as the Apache/Mail/Services
        // sections already do - otherwise the real-time update silently never paints.
        _ = InvokeAsync(StateHasChanged);
    }

    private void DispatchTaskOutput(string taskName, string output)
    {
        if (_inspectContainerId is not null && taskName.Contains("inspect"))
            _inspectContent = output;

        if (_composeEditorVisible && taskName.Contains("compose file"))
        {
            _composeEditorContent = output;
            _composeFileLoading = false;
        }

        if (_shellContainerId is not null && taskName.Contains("exec"))
        {
            _shellOutput += output + "\n";
            _ = JS.InvokeVoidAsync("dockerInterop.scrollToBottom", "docker-shell-output");
        }

        if (_envContainerId is not null && taskName.Contains("env"))
            _envContent = output;

        if (_browseContainerId is not null && taskName.Contains("ls"))
            _browseContent = output;
    }

    private void OnAutoRefreshChanged(bool value)
    {
        _dockerAutoRefresh = value;
        if (value)
        {
            StopAutoRefresh();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
            _autoRefreshCts = cts;
            _autoRefreshTask = RunAutoRefreshAsync(cts);
        }
        else
            StopAutoRefresh();
    }

    private async Task RunAutoRefreshAsync(CancellationTokenSource cts)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
            while (await timer.WaitForNextTickAsync(cts.Token))
                await InvokeAsync(RefreshDockerAsync);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_autoRefreshCts, cts))
            {
                _autoRefreshCts = null;
                _autoRefreshTask = null;
            }
            cts.Dispose();
        }
    }

    private void StopAutoRefresh()
    {
        _autoRefreshCts?.Cancel();
    }

    private async Task RefreshDockerAsync()
    {
        if (_disposed || !await _refreshGate.WaitAsync(0)) return;
        var serverId = ServerId;
        var serverSnapshot = Server;
        _dockerRefreshing = true;
        try
        {
            var containers = await Api.GetDockerContainersAsync(serverId, _lifetimeCts.Token);
            if (_disposed || _activeServerId != serverId || ServerId != serverId) return;
            var updated = serverSnapshot with { Docker = serverSnapshot.Docker with { Containers = containers } };
            await ServerChanged.InvokeAsync(updated);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested) { }
        catch (HttpRequestException) { } // 401 on expired JWT - redirect handled by AuthProvider
        finally
        {
            if (_activeServerId == serverId) _dockerRefreshing = false;
            _refreshGate.Release();
        }
    }

    private async Task DockerActionAsync(string containerId, DockerContainerAction action)
    {
        _dockerActionTarget = containerId;
        var request = new DockerActionRequest { ContainerId = containerId, Action = action };
        var success = await Api.ExecuteDockerActionAsync(ServerId, request);
        if (success)
        {
            Toast.Success("Docker", "DockerActionSent", action, containerId[..Math.Min(12, containerId.Length)]);
        }
        else
        {
            Toast.Error("Docker", "DockerActionFailed", action);
        }
        _dockerActionTarget = null;
    }

    private string? _projectActionTarget;

    private List<DockerContainerDto> ContainersForProject(string project) =>
        Server.Docker.Containers
            .Where(c => string.Equals(c.Project ?? string.Empty, project ?? string.Empty, StringComparison.Ordinal))
            .ToList();

    private async Task ProjectBulkActionAsync(string project, DockerContainerAction action)
    {
        var targets = ContainersForProject(project);
        if (targets.Count == 0) return;

        _projectActionTarget = project;
        var ok = 0;
        foreach (var c in targets)
        {
            var req = new DockerActionRequest { ContainerId = c.ContainerId, Action = action };
            if (await Api.ExecuteDockerActionAsync(ServerId, req)) ok++;
        }
        _projectActionTarget = null;

        if (ok == targets.Count)
            Toast.Success("Docker", "DockerActionSent", action, project);
        else
            Toast.Error("Docker", "DockerActionFailed", $"{action} ({ok}/{targets.Count})");
    }

    private async Task OpenProjectZoom(string project)
    {
        var result = await Dialog.OpenAsync<DockerProjectDialog>(
            $"{L["ZoomProject"]}: {(string.IsNullOrEmpty(project) ? L["NoProject"] : project)}",
            new Dictionary<string, object?> { { "Server", Server }, { "Project", project } },
            new DialogOptions { Width = "90vw", Height = "85vh", Resizable = true, Draggable = true });

        if (result is DockerContainerAction action)
            await ProjectBulkActionAsync(project, action);
    }

    private async Task ConfirmRemoveContainerAsync(string containerId, string name)
    {
        var confirmed = await Dialog.Confirm(
            string.Format(L["RemoveContainerConfirm"].Value, name),
            L["RemoveContainer"].Value,
            new ConfirmOptions { OkButtonText = L["Confirm"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed == true)
            await DockerActionAsync(containerId, DockerContainerAction.Remove);
    }

    private async Task ToggleLogsAsync(string containerId)
    {
        if (_logsContainerId == containerId)
        {
            _logsContainerId = null;
            _logsContent = null;
            return;
        }

        _logsContainerId = containerId;
        _logsContent = null;
        var request = new DockerContainerLogsRequest { ContainerId = containerId, Tail = 100 };
        _logsContent = await Api.GetContainerLogsAsync(ServerId, request);
    }

    private async Task PullImageAsync()
    {
        if (string.IsNullOrWhiteSpace(_pullImageName)) return;
        _imagePulling = true;
        var request = new DockerPullImageRequest { Image = _pullImageName.Trim() };
        var success = await Api.PullDockerImageAsync(ServerId, request);
        if (success)
        {
            Toast.Success("Docker", "DockerPullQueued", _pullImageName);
            _pullImageName = string.Empty;
        }
        else
        {
            Toast.Error("Docker", "DockerPullFailed");
        }
        _imagePulling = false;
    }

    private async Task ConfirmRemoveImageAsync(DockerImageDto image)
    {
        var label = string.IsNullOrWhiteSpace(image.Repository)
            ? image.ImageId[..Math.Min(12, image.ImageId.Length)]
            : $"{image.Repository}:{image.Tag}";
        var confirmed = await Dialog.Confirm(
            string.Format(L["DockerRemoveImageConfirm"].Value, label),
            L["Delete"].Value,
            new ConfirmOptions { OkButtonText = L["Delete"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed == true)
            await RemoveImageAsync(image.ImageId);
    }

    private async Task RemoveImageAsync(string imageId)
    {
        var success = await Api.RemoveDockerImageAsync(ServerId, imageId);
        if (success)
        {
            Toast.Success("Docker", "DockerRemoveImageQueued", imageId[..Math.Min(12, imageId.Length)]);
        }
        else
        {
            Toast.Error("Docker", "DockerRemoveImageFailed");
        }
    }

    private async Task ComposeActionAsync(string stackName, DockerComposeAction action)
    {
        var request = new DockerComposeActionRequest { StackName = stackName, Action = action };
        var success = await Api.ExecuteComposeActionAsync(ServerId, request);
        if (success)
        {
            Toast.Success("DockerCompose", "DockerComposeQueued", action, stackName);
        }
        else
        {
            Toast.Error("DockerCompose", "DockerComposeFailed", action, stackName);
        }
    }

    private async Task OpenPruneDialogAsync()
    {
        var result = await Dialog.OpenAsync<DockerPruneDialog>(
            L["DockerPruneDialog"].Value,
            options: new DialogOptions { Width = "32rem" });
        if (result is DockerPruneDialogResult selection)
            await PruneAsync(selection.Containers, selection.Images, selection.Volumes);
    }

    private async Task PruneAsync(bool containers, bool images, bool volumes)
    {
        _pruning = true;
        var request = new DockerPruneRequest { Containers = containers, Images = images, Volumes = volumes };
        var success = await Api.PruneDockerAsync(ServerId, request);
        if (success)
        {
            Toast.Success("DockerPrune", "DockerPruneQueued");
        }
        else
        {
            Toast.Error("DockerPrune", "DockerPruneFailed");
        }
        _pruning = false;
    }

    private async Task OpenResourceLimitsDialog(string containerId)
    {
        var container = Server.Docker.Containers.FirstOrDefault(c => c.ContainerId == containerId);
        var result = await Dialog.OpenAsync<DockerResourceLimitsDialog>(
            L["ResourceLimits"].Value,
            new Dictionary<string, object?>
            {
                { "ContainerId", containerId },
                { "CpuLimit", 0d },
                { "MemoryLimitMb", (int)(container?.MemoryLimitMb ?? 0) }
            },
            new DialogOptions { Width = "32rem" });
        if (result is not DockerResourceLimitsDialogResult limits) return;

        var request = new DockerResourceLimitsRequest
        {
            ContainerId = containerId,
            CpuLimit = limits.CpuLimit,
            MemoryLimitMb = limits.MemoryLimitMb
        };
        var success = await Api.UpdateDockerResourceLimitsAsync(ServerId, request);
        if (success)
        {
            Toast.Success("Docker", "ResourceLimitsQueued");
        }
        else
        {
            Toast.Error("Docker", "ResourceLimitsFailed");
        }
    }

    private void OnContainerRowRender(RowRenderEventArgs<DockerContainerDto> args)
    {
        if (args.Attributes is null || args.Data is null) return;
        var stateClass = args.Data.State switch
        {
            "running" => "docker-row-running",
            "exited" => "docker-row-exited",
            "paused" => "docker-row-paused",
            _ => "docker-row-other"
        };
        var projectClass = GetProjectAccentClass(args.Data.Project);
        args.Attributes["class"] = string.IsNullOrEmpty(projectClass)
            ? stateClass
            : $"{stateClass} docker-row-project {projectClass}";
    }

    internal static string GetProjectAccentClass(string? project)
    {
        if (string.IsNullOrWhiteSpace(project)) return string.Empty;
        var hash = 0;
        foreach (var c in project) hash = unchecked(hash * 31 + c);
        return $"docker-project-accent-{Math.Abs(hash) % 8}";
    }

    private static BadgeStyle GetContainerBadge(string state) => state switch
    {
        "running" => BadgeStyle.Success,
        "exited" => BadgeStyle.Danger,
        "paused" => BadgeStyle.Warning,
        "restarting" => BadgeStyle.Info,
        _ => BadgeStyle.Light
    };

    private static string GetMemoryBarClass(DockerContainerDto c)
    {
        if (c.MemoryLimitMb <= 0) return "docker-mem-bar";
        var pct = c.MemoryUsageMb / c.MemoryLimitMb * 100;
        return pct switch
        {
            >= 90 => "docker-mem-bar docker-mem-red",
            >= 70 => "docker-mem-bar docker-mem-yellow",
            _ => "docker-mem-bar docker-mem-green"
        };
    }

    private async Task RequestInspectAsync(string containerId)
    {
        if (_inspectContainerId == containerId)
        {
            _inspectContainerId = null;
            _inspectContent = null;
            return;
        }

        _inspectContainerId = containerId;
        _inspectContent = null;
        var success = await Api.InspectContainerAsync(ServerId, containerId);
        if (!success)
        {
            Toast.Error("Docker", "DockerInspectFailed");
            _inspectContainerId = null;
        }
    }

    private async Task OpenComposeEditorAsync(string stackName)
    {
        _composeEditorStack = stackName;
        _composeEditorContent = string.Empty;
        _composeEditorVisible = true;
        _composeFileLoading = true;
        var success = await Api.GetComposeFileAsync(ServerId, stackName);
        if (!success)
        {
            Toast.Error("DockerCompose", "DockerComposeFileFailed");
            _composeFileLoading = false;
        }
    }

    private void CloseComposeEditor()
    {
        _composeEditorVisible = false;
        _composeEditorStack = string.Empty;
        _composeEditorContent = string.Empty;
    }

    private async Task SaveComposeFileAsync()
    {
        if (string.IsNullOrWhiteSpace(_composeEditorContent)) return;
        _composeDeploying = true;
        var request = new DockerComposeFileSaveRequest
        {
            StackName = _composeEditorStack,
            Content = _composeEditorContent
        };
        var success = await Api.SaveComposeFileAsync(ServerId, request);
        if (success)
        {
            Toast.Success("DockerCompose", "DockerComposeDeployQueued", _composeEditorStack);
            CloseComposeEditor();
        }
        else
        {
            Toast.Error("DockerCompose", "DockerComposeDeployFailed");
        }
        _composeDeploying = false;
    }

    private void OpenShell(string containerId, string name)
    {
        if (_shellContainerId == containerId)
        {
            _shellContainerId = null;
            _shellOutput = string.Empty;
            return;
        }

        _shellContainerId = containerId;
        _shellContainerName = name;
        _shellCommand = string.Empty;
        _shellOutput = string.Empty;
    }

    private async Task SendShellCommandAsync()
    {
        if (string.IsNullOrWhiteSpace(_shellCommand) || _shellContainerId is null) return;
        _shellOutput += $"$ {_shellCommand}\n";
        var request = new DockerExecRequest { ContainerId = _shellContainerId, Command = _shellCommand };
        var success = await Api.ExecuteShellCommandAsync(ServerId, request);
        if (!success)
        {
            _shellOutput += L["DockerExecFailed"].Value + "\n";
        }
        _shellCommand = string.Empty;
    }

    private async Task OnShellKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter") await SendShellCommandAsync();
    }

    private async Task CopyInspectToClipboardAsync()
    {
        if (_inspectContent is null) return;
        await Clipboard.CopyAsync(_inspectContent, L["CopiedToClipboard"]);
    }

    private async Task RequestEnvVarsAsync(string containerId, string name)
    {
        if (_envContainerId == containerId)
        {
            _envContainerId = null;
            _envContent = null;
            return;
        }

        _envContainerId = containerId;
        _envContainerName = name;
        _envContent = null;
        var success = await Api.GetContainerEnvVarsAsync(ServerId, containerId);
        if (!success)
        {
            Toast.Error("Docker", "DockerEnvVarsFailed");
            _envContainerId = null;
        }
    }

    private static List<DockerEnvVarDto> ParseEnvVars(string json)
    {
        var result = new List<DockerEnvVarDto>();

        // Docker reports a container's env as a JSON array of "KEY=VALUE" strings,
        // where VALUE may itself contain '=' and ',' (PATH, connection strings, base64).
        // Deserialize the array with System.Text.Json and split each entry on the
        // FIRST '=' only so those values survive intact.
        string[]? entries;
        try
        {
            entries = JsonSerializer.Deserialize<string[]>(json);
        }
        catch (JsonException)
        {
            return result;
        }

        if (entries is null) return result;

        foreach (var entry in entries)
        {
            if (entry is null) continue;
            var eqIndex = entry.IndexOf('=');
            if (eqIndex > 0)
            {
                result.Add(new DockerEnvVarDto
                {
                    Key = entry[..eqIndex],
                    Value = entry[(eqIndex + 1)..]
                });
            }
        }
        return result;
    }

    private async Task OpenFileBrowserAsync(string containerId, string name)
    {
        if (_browseContainerId == containerId)
        {
            _browseContainerId = null;
            _browseContent = null;
            return;
        }

        _browseContainerId = containerId;
        _browseContainerName = name;
        _browsePath = "/";
        _browseContent = null;
        await BrowsePathAsync();
    }

    private async Task BrowsePathAsync()
    {
        _browseContent = null;
        var request = new DockerBrowseRequest { ContainerId = _browseContainerId!, Path = _browsePath };
        var success = await Api.ListContainerFilesAsync(ServerId, request);
        if (!success)
        {
            Toast.Error("Docker", "DockerBrowseFailed");
            _browseContainerId = null;
        }
    }

    private async Task BrowseParentAsync()
    {
        if (_browsePath == "/") return;
        var lastSlash = _browsePath.TrimEnd('/').LastIndexOf('/');
        _browsePath = lastSlash <= 0 ? "/" : _browsePath[..lastSlash];
        await BrowsePathAsync();
    }

    private async Task BuildImageAsync()
    {
        if (string.IsNullOrWhiteSpace(_buildImageTag) || string.IsNullOrWhiteSpace(_buildDockerfileContent)) return;
        _buildingImage = true;
        var request = new DockerBuildRequest
        {
            ImageTag = _buildImageTag.Trim(),
            DockerfileContent = _buildDockerfileContent
        };
        var success = await Api.BuildImageAsync(ServerId, request);
        if (success)
        {
            Toast.Success("DockerBuild", "DockerBuildQueued", _buildImageTag);
            _buildImageTag = string.Empty;
            _buildDockerfileContent = string.Empty;
        }
        else
        {
            Toast.Error("DockerBuild", "DockerBuildFailed");
        }
        _buildingImage = false;
    }

    private void ResetServerState()
    {
        _dockerRefreshing = false;
        _dockerActionTarget = null;
        _projectActionTarget = null;
        _logsContainerId = null;
        _logsContent = null;
        _inspectContainerId = null;
        _inspectContent = null;
        _composeEditorVisible = false;
        _composeEditorStack = string.Empty;
        _composeEditorContent = string.Empty;
        _composeFileLoading = false;
        _shellContainerId = null;
        _shellContainerName = string.Empty;
        _shellCommand = string.Empty;
        _shellOutput = string.Empty;
        _envContainerId = null;
        _envContainerName = string.Empty;
        _envContent = null;
        _browseContainerId = null;
        _browseContainerName = string.Empty;
        _browsePath = "/";
        _browseContent = null;
        _containerSearch = string.Empty;
        _imageSearch = string.Empty;
        _composeSearch = string.Empty;
        _networkSearch = string.Empty;
        _volumeSearch = string.Empty;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetimeCts.Cancel();
        StopAutoRefresh();
        var autoRefreshTask = _autoRefreshTask;
        if (autoRefreshTask is not null)
            await autoRefreshTask;
        await _refreshGate.WaitAsync();
        _refreshGate.Release();
        _lifetimeCts.Dispose();
        _refreshGate.Dispose();
    }
}
