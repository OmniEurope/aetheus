// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Helpers;
using Aetheus.Front.Pages.Pipelines;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Shared;

/// <summary>
/// 0-a: single shared Releases list, reused across the 3 scopes (global <c>/releases</c>, project
/// detail, server detail) instead of three diverged copies. Filter is driven by the optional
/// <see cref="ProjectId"/> / <see cref="ServerId"/> parameters:
/// <list type="bullet">
/// <item>both null = global, server-paginated, with a project filter dropdown;</item>
/// <item><see cref="ProjectId"/> set = project scope, server-paginated, Project column hidden;</item>
/// <item><see cref="ServerId"/> set = server scope, self-loaded via <c>GetServerReleasesAsync</c>
/// (no server-side serverId paging exists yet) and paged in-memory.</item>
/// </list>
/// Self-loading (the section fetches its own data) - no dependency on the detail loaders. A
/// stale-while-revalidate <see cref="ListCacheService"/> entry renders the last list instantly on
/// revisit while a fresh fetch runs behind.
/// </summary>
public partial class ReleasesList : IAsyncDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private HubConnectionFactory HubFactory { get; set; } = default!;
    [Inject] private PermissionService Permissions { get; set; } = default!;
    [Inject] private ListCacheService Cache { get; set; } = default!;
    [Inject] private ProjectDetailLoader ProjectLoader { get; set; } = default!;

    /// <summary>Project filter (project detail scope). Mutually exclusive with <see cref="ServerId"/>.</summary>
    [Parameter] public int? ProjectId { get; set; }

    /// <summary>Server filter (server detail scope). Mutually exclusive with <see cref="ProjectId"/>.</summary>
    [Parameter] public int? ServerId { get; set; }

    private bool IsServerScope => ServerId.HasValue;
    private bool IsProjectScope => ProjectId.HasValue;
    private bool IsGlobal => !ProjectId.HasValue && !ServerId.HasValue;

    private HubConnection? _hubConnection;
    private List<ReleaseDto> _releases = [];
    private List<ReleaseDto> _serverAll = []; // server scope: full list held in memory, paged client-side
    private List<ProjectDto> _projects = [];
    private int _totalCount;
    private bool _loading;
    private bool _syncing;
    private bool _canWrite;
    private int? _projectFilter; // global scope only

    private const int ChangelogPreviewLength = 120;
    private readonly HashSet<int> _expandedChangelogs = [];

    private string CacheKey => IsServerScope ? $"releases:server:{ServerId}"
        : IsProjectScope ? $"releases:project:{ProjectId}"
        : "releases:global";

    // Server-paginated (global/project) scope cache key, distinct from the client-scope CacheKey above.
    private string PagedCacheKey(int page, int pageSize, int? projectId) =>
        $"releases:paged:{projectId}:{page}:{pageSize}";

    private void ApplyReleasesPage(PaginatedResult<ReleaseDto> result)
    {
        _releases = result.Items;
        _totalCount = result.TotalCount;
    }

    protected override async Task OnInitializedAsync()
    {
        Permissions.OnPermissionsChanged += OnPermissionsChanged;
        RefreshCanWrite();

        // Stale-while-revalidate: render the last known server-scope list immediately.
        if (IsServerScope && Cache.TryGet<List<ReleaseDto>>(CacheKey, out var cached) && cached is not null)
        {
            _serverAll = cached;
            _totalCount = cached.Count;
        }
        else if (!IsServerScope)
        {
            // Pre-seed the paginated view so the first paint is instant.
            Cache.Seed<PaginatedResult<ReleaseDto>>(PagedCacheKey(1, 25, ProjectId ?? _projectFilter), ApplyReleasesPage);
        }

        try
        {
            if (IsGlobal)
            {
                var projectsResult = await Api.GetProjectsAsync(pageSize: 100);
                _projects = projectsResult.Items;
            }
            if (IsServerScope)
                await LoadServerReleasesAsync();
        }
        catch (HttpRequestException) { _projects = []; }

        // #8: on a project detail page the ProjectDetailLoader already runs a "releases" hub (for the
        // Overview tile). Reuse it in project scope - subscribe to its OnChanged and re-page the grid -
        // instead of opening a SECOND WebSocket to the same hub. Global/server scopes have no loader, so
        // they keep their own hub.
        if (IsProjectScope)
            ProjectLoader.OnChanged += OnProjectLoaderChanged;
        else
            await ConnectToHub();
    }

    private void OnProjectLoaderChanged() => InvokeAsync(ReloadGrid);

    private async Task LoadServerReleasesAsync()
    {
        _serverAll = await Api.GetServerReleasesAsync(ServerId!.Value);
        _totalCount = _serverAll.Count;
        Cache.Set(CacheKey, _serverAll);
    }

    private void OnPermissionsChanged()
    {
        RefreshCanWrite();
        InvokeAsync(StateHasChanged);
    }

    private void RefreshCanWrite() => _canWrite = Permissions.CanWrite(ResourceType.Release);

    // Server scope pages the in-memory list; global/project scopes page server-side via the API.
    private async Task OnLoadData(LoadDataArgs args)
    {
        if (IsServerScope)
        {
            // Server-detail scope: page the in-memory list (already cached via LoadServerReleasesAsync).
            var skip = args.Skip ?? 0;
            var top = args.Top ?? 25;
            _releases = _serverAll.Skip(skip).Take(top).ToList();
            _totalCount = _serverAll.Count;
            return;
        }

        var (page, pageSize) = args.ToPageRequest();
        var projectId = ProjectId ?? _projectFilter;
        await Cache.RevalidateAsync(
            PagedCacheKey(page, pageSize, projectId),
            () => Api.GetReleasesAsync(page: page, pageSize: pageSize, projectId: projectId),
            ApplyReleasesPage,
            loading => _loading = loading,
            () => InvokeAsync(StateHasChanged));
    }

    // Manual refresh: re-fetch server scope from source, then reload the grid for paginated scopes.
    private async Task RefreshAsync()
    {
        if (IsServerScope)
        {
            try { await LoadServerReleasesAsync(); }
            catch (HttpRequestException) { }
        }
        await ReloadGrid();
    }

    private async Task OnProjectFilterChanged()
    {
        await ReloadGrid();
    }

    private async Task ClearFilters()
    {
        _projectFilter = null;
        await ReloadGrid();
    }

    private async Task SyncReleases()
    {
        // Global syncs the selected project; project scope syncs its own project.
        var targetProject = IsProjectScope ? ProjectId : _projectFilter;
        if (!targetProject.HasValue) return;

        _syncing = true;
        var synced = await Api.SyncReleasesAsync(targetProject.Value);
        if (synced.Count == 0)
            Toast.Info("Synced", "NoNewReleasesFound");
        else
            Toast.Success("Synced", "ReleasesSynced", synced.Count);
        await ReloadGrid();
        _syncing = false;
    }

    private async Task TriggerBuild(ReleaseDto release)
    {
        var pipelines = await Api.GetPipelinesAsync(pageSize: 100);
        if (pipelines.Items.Count == 0)
        {
            Toast.Warning("NoPipelinesFound", "NoPipelinesFound");
            return;
        }

        var selectedPipelineId = await Dialog.OpenAsync<PipelineSelectDialog>(
            L["SelectPipeline"].Value,
            new Dictionary<string, object?> { { "Pipelines", pipelines.Items } },
            new DialogOptions { Width = "400px" });

        if (selectedPipelineId is int pipelineId)
        {
            var result = await Api.TriggerReleaseBuildAsync(release.Id, new TriggerReleaseBuildRequest { PipelineId = pipelineId });
            if (result is not null)
            {
                Toast.Success("BuildTriggered", release.Version);
                await ReloadGrid();
            }
        }
    }

    private async Task RollbackRelease(ReleaseDto release)
    {
        var preview = await Api.GetRollbackPreviewAsync(release.Id);
        if (preview is null || !preview.CanRollback || string.IsNullOrWhiteSpace(preview.TargetVersion))
        {
            Toast.Warning("Rollback", preview?.Reason ?? "RollbackUnavailable");
            return;
        }

        var pipelines = await Api.GetPipelinesAsync(pageSize: 100, projectId: release.ProjectId);
        var request = await Dialog.OpenAsync<RollbackReleaseDialog>(
            L["Rollback"].Value,
            new Dictionary<string, object?>
            {
                ["SourceVersion"] = release.Version,
                ["TargetVersion"] = preview.TargetVersion,
                ["DeploymentAge"] = RelativeTime.FormatAgo(L, release.PublishedAt ?? release.DetectedAt),
                ["ProjectId"] = release.ProjectId,
                ["Pipelines"] = pipelines.Items
            },
            new DialogOptions { Width = "520px" });
        if (request is not RollbackReleaseRequest rollbackRequest) return;

        var result = await Api.RollbackReleaseAsync(release.Id, rollbackRequest);
        if (result is not null)
        {
            Toast.Success("RollbackQueued", release.Version);
            await ReloadGrid();
        }
    }

    private async Task PromoteRelease(ReleaseDto release)
    {
        var confirmed = await Dialog.Confirm(
            string.Format(L["PromoteConfirm"].Value, release.Version),
            L["Promote"].Value,
            new ConfirmOptions { OkButtonText = L["Promote"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed != true) return;

        var result = await Api.PromoteReleaseAsync(release.Id);
        if (result is not null)
        {
            Toast.Success("Promoted", release.Version);
            await ReloadGrid();
        }
    }

    // --- Changelog cell expand/collapse (ported from the project section) ---
    private bool IsChangelogExpanded(int releaseId) => _expandedChangelogs.Contains(releaseId);

    private void ToggleChangelog(int releaseId)
    {
        if (!_expandedChangelogs.Remove(releaseId))
            _expandedChangelogs.Add(releaseId);
    }

    private static bool IsChangelogTruncatable(string changelog) => changelog.Length > ChangelogPreviewLength;

    private static string ChangelogPreview(string changelog) =>
        changelog.Length <= ChangelogPreviewLength ? changelog : changelog[..ChangelogPreviewLength] + "…";

    // --- Live refresh ---
    private bool _hubReloading;

    private async Task ConnectToHub()
    {
        _hubConnection = HubFactory.Create("releases");
        // R3W9: a batch update can carry deletes/reorders across a whole project; a blind row-patch would
        // silently mask a removed release (the finding's own caveat), so this path stays an authoritative
        // reload. A single create is a pure ADD - patched in place below.
        _hubConnection.On<int, List<ReleaseDto>>("ReleasesUpdated", (_, _) => HubReloadAsync());
        _hubConnection.On<ReleaseDto>("ReleaseCreated", OnReleaseCreated);
        _hubConnection.On<ReleaseDto>("ReleaseStatusChanged", OnReleaseStatusChanged);
        // Group membership is per-connection and lost on auto-reconnect - re-join + reload.
        _hubConnection.RejoinOnReconnect(async () =>
        {
            await _hubConnection.InvokeAsync("JoinAllReleases");
            await HubReloadAsync();
        });

        try
        {
            await _hubConnection.StartAsync();
            await _hubConnection.InvokeAsync("JoinAllReleases");
        }
        catch { /* Hub unavailable - degrade to static */ }
    }

    // R3W9: a status change only mutates an existing release - patch that one row via the record's
    // `with`-style replace and re-render just the grid (mirrors the Servers heartbeat patch), instead
    // of a full LoadData round-trip. Falls back to a reload when the release isn't on the visible page
    // (or in the server-scope in-memory list), where a targeted patch can't stay consistent.
    private Task OnReleaseStatusChanged(ReleaseDto release)
    {
        var pageIdx = _releases.FindIndex(r => r.Id == release.Id);
        var allIdx = IsServerScope ? _serverAll.FindIndex(r => r.Id == release.Id) : -1;
        if (pageIdx < 0 && allIdx < 0)
            return HubReloadAsync();

        return InvokeAsync(() =>
        {
            if (pageIdx >= 0) _releases[pageIdx] = release;
            if (allIdx >= 0) _serverAll[allIdx] = release;
            StateHasChanged();
        });
    }

    // R3W9: a freshly created release is a single ADD. Releases render newest-first, so the new row belongs
    // at the top - prepend it to the visible page and the server-scope in-memory list (upsert if a status
    // event already inserted it), instead of a full LoadData round-trip.
    private Task OnReleaseCreated(ReleaseDto release) =>
        InvokeAsync(() =>
        {
            if ((IsProjectScope && release.ProjectId != ProjectId)
                || (IsGlobal && _projectFilter is > 0 && release.ProjectId != _projectFilter))
                return;
            if (IsServerScope)
            {
                _ = HubReloadAsync();
                return;
            }
            var j = _releases.FindIndex(r => r.Id == release.Id);
            if (j >= 0) _releases[j] = release; else _releases.Insert(0, release);
            StateHasChanged();
        });

    private Task HubReloadAsync()
    {
        if (_hubReloading) return Task.CompletedTask;
        _hubReloading = true;
        return InvokeAsync(async () =>
        {
            try
            {
                if (IsServerScope) await LoadServerReleasesAsync();
                await ReloadGrid();
            }
            catch (HttpRequestException) { } // hub-triggered reload - silent on auth failure
            finally { _hubReloading = false; }
            StateHasChanged();
        });
    }

    private AetheusDataGrid<ReleaseDto>? _grid;
    private Task ReloadGrid() => _grid?.Reload() ?? Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        Permissions.OnPermissionsChanged -= OnPermissionsChanged;
        if (IsProjectScope)
            ProjectLoader.OnChanged -= OnProjectLoaderChanged;
        if (_hubConnection is not null)
            await _hubConnection.DisposeAsync();
    }
}
