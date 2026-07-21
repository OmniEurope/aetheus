// SPDX-License-Identifier: EUPL-1.2
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
/// 0-a: shared Environments list. Environments aren't server-scoped, so the component supports the
/// global (unfiltered) and project scopes - both server-paginated via <c>GetEnvironmentsAsync</c>
/// (the endpoint supports a <c>projectId</c> filter). Self-loading - no dependency on the detail
/// loaders; the <c>entities</c> hub keeps the grid live.
/// </summary>
public partial class EnvironmentsList : IAsyncDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private PermissionService Permissions { get; set; } = default!;
    [Inject] private HubConnectionFactory HubFactory { get; set; } = default!;
    [Inject] private ListCacheService Cache { get; set; } = default!;

    /// <summary>Project filter (project detail scope). When null the list is global (unfiltered).</summary>
    [Parameter] public int? ProjectId { get; set; }

    private bool IsProjectScope => ProjectId.HasValue;
    private bool IsGlobal => !ProjectId.HasValue;

    private List<EnvironmentDto> _environments = [];
    private int _totalCount;
    private bool _loading;
    private bool _canWrite;
    private string? _search; // global scope only
    private HubConnection? _hubConnection;

    protected override async Task OnInitializedAsync()
    {
        Permissions.OnPermissionsChanged += OnPermissionsChanged;
        RefreshCanWrite();
        // Stale-while-revalidate: pre-seed from the cached default view so the first paint is instant;
        // OnLoadData then revalidates in the background.
        Cache.Seed<PaginatedResult<EnvironmentDto>>(CacheKey(1, 25, null), ApplyEnvironments);
        await StartHubAsync();
    }

    private string CacheKey(int page, int pageSize, string? search) =>
        $"environments:{ProjectId}:{page}:{pageSize}:{search}";

    private void ApplyEnvironments(PaginatedResult<EnvironmentDto> result)
    {
        _environments = result.Items;
        _totalCount = result.TotalCount;
    }

    private void OnPermissionsChanged()
    {
        RefreshCanWrite();
        InvokeAsync(StateHasChanged);
    }

    private void RefreshCanWrite() => _canWrite = Permissions.CanWrite(ResourceType.Environment);

    private async Task OnLoadData(LoadDataArgs args)
    {
        var (page, pageSize) = args.ToPageRequest();
        await Cache.RevalidateAsync(
            CacheKey(page, pageSize, _search),
            () => Api.GetEnvironmentsAsync(page: page, pageSize: pageSize, search: _search, projectId: ProjectId),
            ApplyEnvironments,
            loading => _loading = loading,
            () => InvokeAsync(StateHasChanged));
    }

    private AetheusDataGrid<EnvironmentDto>? _grid;
    private Task ReloadGrid() => _grid?.GoToPage(0) ?? Task.CompletedTask;

    private async Task ClearFilters()
    {
        _search = null;
        await ReloadGrid();
    }

    private void NewEnvironment() => Nav.NavigateTo(IsProjectScope
        ? $"/environments/new?projectId={ProjectId}"
        : "/environments/new");

    private async Task DuplicateEnvironment(EnvironmentDto env)
    {
        var result = await Api.DuplicateEnvironmentAsync(env.Id, new DuplicateEnvironmentRequest { TargetProjectId = env.ProjectId });
        if (result is not null)
        {
            Toast.Success("Created", "EnvironmentDuplicated");
            Nav.NavigateTo($"/environments/{result.Id}");
        }
    }

    private async Task StartHubAsync()
    {
        try
        {
            _hubConnection = HubFactory.Create("entities");
            _hubConnection.On<ResourceType, int, string>("EntityChanged", (type, _, _) =>
            {
                if (type == ResourceType.Environment)
                    return InvokeAsync(ReloadGrid);
                return Task.CompletedTask;
            });
            // Group membership is per-connection and lost on auto-reconnect - re-join + reload.
            _hubConnection.RejoinOnReconnect(() => InvokeAsync(async () =>
            {
                await _hubConnection.InvokeAsync("JoinEntityUpdates", ResourceType.Environment);
                await ReloadGrid();
            }));
            await _hubConnection.StartAsync();
            await _hubConnection.InvokeAsync("JoinEntityUpdates", ResourceType.Environment);
        }
        catch { /* Hub unavailable - degrade to static */ }
    }

    public async ValueTask DisposeAsync()
    {
        Permissions.OnPermissionsChanged -= OnPermissionsChanged;
        if (_hubConnection is not null)
        {
            try { await _hubConnection.InvokeAsync("LeaveEntityUpdates", ResourceType.Environment); } catch { /* best-effort */ }
            await _hubConnection.DisposeAsync();
            _hubConnection = null;
        }
    }
}
