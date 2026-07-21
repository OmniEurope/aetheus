// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Helpers;
using Aetheus.Front.Layout;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.Constants;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using Radzen;

namespace Aetheus.Front.Pages.Projects;

public partial class Projects : IAsyncDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;
    [Inject] private PermissionService Permissions { get; set; } = default!;
    [Inject] private HubConnectionFactory HubFactory { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private ListCacheService Cache { get; set; } = default!;

    private List<ProjectDto> _projects = [];
    private int _totalCount;
    private bool _loading;
    private bool _canWrite;
    private string? _search;
    private ProjectStatus? _statusFilter;

    private List<object> _statusOptions = [];
    private HubConnection? _hubConnection;
    private HubConnection? _pipelineHub;
    private readonly TrailingReloadCoalescer _pipelineReload = new(1000);

    private string CacheKey => $"projects:{_search}:{_statusFilter}";

    private void ApplyProjects(PaginatedResult<ProjectDto> result)
    {
        _projects = result.Items;
        _totalCount = result.TotalCount;
    }

    protected override async Task OnInitializedAsync()
    {
        _statusOptions =
        [
            new { Text = L["Active"].Value, Value = (ProjectStatus?)ProjectStatus.Active },
            new { Text = L["Archived"].Value, Value = (ProjectStatus?)ProjectStatus.Archived }
        ];
        Breadcrumb.Set(new BreadcrumbItem(L["Projects"]));
        // MainLayout loads permissions in parallel with route activation, so this
        // page can mount BEFORE Permissions.IsLoaded - capturing _canWrite=false
        // would then strand the "New Project" button as permanently disabled.
        // Subscribe to the change event so the button reactivates the moment
        // permissions land. (The race only bites cold-boot navigations like the
        // ones the E2E suite drives, but the bug is real for users too.)
        Permissions.OnPermissionsChanged += OnPermissionsChanged;
        RefreshCanWrite();
        Cache.Seed<PaginatedResult<ProjectDto>>(CacheKey, ApplyProjects);
        try
        {
            await LoadData();
        }
        catch (HttpRequestException) { } // 401 on expired JWT - redirect handled by AuthProvider
        await StartHubAsync();
    }

    private void OnPermissionsChanged()
    {
        RefreshCanWrite();
        InvokeAsync(StateHasChanged);
    }

    private void RefreshCanWrite() =>
        _canWrite = Permissions.CanWrite(Aetheus.Shared.Enums.ResourceType.Project);

    private async Task StartHubAsync()
    {
        try
        {
            _hubConnection = HubFactory.Create("entities");
            _hubConnection.On<ResourceType, int, string>("EntityChanged", (type, _, _) =>
            {
                if (type == ResourceType.Project)
                    return InvokeAsync(LoadDataAndRender);
                return Task.CompletedTask;
            });
            _hubConnection.RejoinOnReconnect(() => InvokeAsync(async () =>
            {
                await _hubConnection.InvokeAsync("JoinEntityUpdates", ResourceType.Project);
                await LoadDataAndRender();
            }));
            await _hubConnection.StartAsync();
            await _hubConnection.InvokeAsync("JoinEntityUpdates", ResourceType.Project);
        }
        catch
        {
            // best-effort: SignalR is non-critical for the page.
        }

        try
        {
            _pipelineHub = HubFactory.Create("pipelines");
            _pipelineHub.On<int, int>("PipelineRunStarted", (_, _) => RequestPipelineReloadAsync());
            _pipelineHub.On<int, PipelineStatus>("PipelineRunCompleted", (_, _) => RequestPipelineReloadAsync());
            _pipelineHub.On<int>("PipelineRunCancelled", _ => RequestPipelineReloadAsync());
            _pipelineHub.On<int, string, string>("ApprovalRequired", (_, _, _) => RequestPipelineReloadAsync());
            _pipelineHub.On<int, string, string>("ApprovalResolved", (_, _, _) => RequestPipelineReloadAsync());
            _pipelineHub.RejoinOnReconnect(async () =>
            {
                await _pipelineHub.InvokeAsync("JoinPipelineUpdatesGroup");
                await LoadDataAndRender();
            });
            await _pipelineHub.StartAsync();
            await _pipelineHub.InvokeAsync("JoinPipelineUpdatesGroup");
        }
        catch { /* best-effort: the project list remains usable without live updates */ }
    }

    private Task RequestPipelineReloadAsync() =>
        InvokeAsync(() => _pipelineReload.RequestAsync(() => InvokeAsync(LoadDataAndRender)));

    private async Task LoadDataAndRender()
    {
        await LoadData();
        StateHasChanged();
    }

    public async ValueTask DisposeAsync()
    {
        Permissions.OnPermissionsChanged -= OnPermissionsChanged;
        if (_hubConnection is not null)
        {
            try { await _hubConnection.InvokeAsync("LeaveEntityUpdates", ResourceType.Project); } catch { /* best-effort */ }
            await _hubConnection.DisposeAsync();
            _hubConnection = null;
        }
        if (_pipelineHub is not null)
        {
            try { await _pipelineHub.InvokeAsync("LeavePipelineUpdatesGroup"); } catch { /* best-effort */ }
            await _pipelineHub.DisposeAsync();
            _pipelineHub = null;
        }
    }

    private async Task LoadData()
    {
        await Cache.RevalidateAsync(
            CacheKey,
            () => Api.GetProjectsAsync(search: _search, status: _statusFilter),
            ApplyProjects,
            loading => _loading = loading,
            () => InvokeAsync(StateHasChanged));
    }

    private async Task ClearFilters()
    {
        _search = null;
        _statusFilter = null;
        await LoadData();
    }

    /// <summary>Opens the create-project dialog; reloads the list when a project is created.</summary>
    private async Task OpenCreateDialog()
    {
        var result = await Dialog.OpenAsync<ProjectFormDialog>(
            L["NewProject"],
            new Dictionary<string, object?>(),
            ProjectFormDialog.DialogOptions());
        if (result is ProjectDto created)
        {
            await LoadDataAndRender();
            // M9DZ: creating from the list keeps you on the list - offer to jump straight into the
            // freshly created project rather than making the user hunt for it in the grid.
            var open = await Dialog.Confirm(
                string.Format(L["OpenNewProjectConfirm"].Value, created.Name),
                L["ProjectCreated"].Value,
                new ConfirmOptions { OkButtonText = L["OpenProject"].Value, CancelButtonText = L["StayHere"].Value });
            if (open == true)
                Nav.NavigateTo($"/projects/{created.Id}/overview");
        }
    }

    // B3KP: the "Repository" chip. An internal-mirror URL (this control plane's own
    // /git/{id}/*.git smart-HTTP clone endpoint - a git protocol path, not a browsable page)
    // deep-links to the internal git browser; a genuine external URL opens in a new tab.
    private ValueTask OpenRepositoryAsync(ProjectDto project)
    {
        if (IsInternalMirrorUrl(project.RepositoryUrl))
        {
            Nav.NavigateTo($"/git-repositories?projectId={project.Id}");
            return ValueTask.CompletedTask;
        }
        return JS.InvokeVoidAsync("open", project.RepositoryUrl!, "_blank", "noopener");
    }

    /// <summary>True when <paramref name="url"/> is one of our internal smart-HTTP mirror clone URLs
    /// (<c>/git/{projectId}/{slug}.git</c>) rather than an external repository the browser can open.</summary>
    internal static bool IsInternalMirrorUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        var path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;
        return MirrorRepoPath.IsMirrorPath(path);
    }

    // B3KP: the git chip jumps to the project overview, which hosts the changelog/history tab.
    private void OpenGitHistory(int projectId) => Nav.NavigateTo($"/projects/{projectId}/overview");

    private static string TruncateText(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        return string.Concat(text.AsSpan(0, maxLength - 3), "...");
    }
}
