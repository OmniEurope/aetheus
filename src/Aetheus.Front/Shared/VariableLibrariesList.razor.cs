// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Shared;

/// <summary>
/// 0-a: shared Variable Libraries list, reused across the 3 scopes. The optional
/// <see cref="ProjectId"/> / <see cref="ServerId"/> parameters pick the source: global/project are
/// server-paginated via <c>GetVariableLibrariesAsync</c> (the endpoint supports a <c>projectId</c>
/// filter); server detail is self-loaded via <c>GetServerVariableLibrariesAsync</c> and paged
/// in-memory (no server-side serverId paging) with a <see cref="ListCacheService"/> entry for
/// stale-while-revalidate. Self-loading - no dependency on the detail loaders.
/// </summary>
public partial class VariableLibrariesList : IAsyncDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private PermissionService Permissions { get; set; } = default!;
    [Inject] private HubConnectionFactory HubFactory { get; set; } = default!;
    [Inject] private ListCacheService Cache { get; set; } = default!;

    /// <summary>Project filter (project detail scope). Mutually exclusive with <see cref="ServerId"/>.</summary>
    [Parameter] public int? ProjectId { get; set; }

    /// <summary>Server filter (server detail scope). When both are null the list is global (unfiltered).</summary>
    [Parameter] public int? ServerId { get; set; }

    private bool IsServerScope => ServerId.HasValue;
    private bool IsProjectScope => ProjectId.HasValue;
    private bool IsGlobal => !ProjectId.HasValue && !ServerId.HasValue;

    private List<VariableLibraryDto> _libraries = [];
    private List<VariableLibraryDto> _serverAll = []; // server scope: full list, paged client-side
    private int _totalCount;
    private bool _loading;
    private bool _canWrite;
    private string? _search; // global scope only
    private HubConnection? _hubConnection;

    private string CacheKey => $"variable-libraries:server:{ServerId}";

    // Server-paginated (global/project) scope cache key, distinct from the server-detail CacheKey above.
    // The sort is part of the key: two orders sharing one entry means the second is served the first
    // one's rows, which is indistinguishable from a sort that does nothing.
    private string PagedCacheKey(int page, int pageSize, string? search, string? sortBy = null, bool sortDescending = false) =>
        $"variable-libraries:{ProjectId}:{page}:{pageSize}:{search}:{sortBy}:{sortDescending}";

    private void ApplyLibrariesPage(PaginatedResult<VariableLibraryDto> result)
    {
        _libraries = result.Items;
        _totalCount = result.TotalCount;
    }

    protected override async Task OnInitializedAsync()
    {
        Permissions.OnPermissionsChanged += OnPermissionsChanged;
        RefreshCanWrite();

        if (IsServerScope && Cache.TryGet<List<VariableLibraryDto>>(CacheKey, out var cached) && cached is not null)
        {
            _serverAll = cached;
            _totalCount = cached.Count;
        }
        else if (!IsServerScope)
        {
            // Stale-while-revalidate: pre-seed the paginated view so the first paint is instant.
            Cache.Seed<PaginatedResult<VariableLibraryDto>>(PagedCacheKey(1, 25, null), ApplyLibrariesPage);
        }

        if (IsServerScope)
        {
            try { await LoadServerLibrariesAsync(); }
            catch (HttpRequestException) { } // 401 on expired JWT - redirect handled by AuthProvider
        }

        await StartHubAsync();
    }

    private async Task LoadServerLibrariesAsync()
    {
        _serverAll = await Api.Servers.GetServerVariableLibrariesAsync(ServerId!.Value);
        _totalCount = _serverAll.Count;
        Cache.Set(CacheKey, _serverAll);
    }

    private void OnPermissionsChanged()
    {
        RefreshCanWrite();
        InvokeAsync(StateHasChanged);
    }

    private void RefreshCanWrite() => _canWrite = Permissions.CanWrite(ResourceType.VariableLibrary);

    private async Task OnLoadData(LoadDataArgs args)
    {
        if (TryApplyServerPage(args)) return;

        var (page, pageSize) = args.ToPageRequest();
        var (sortBy, sortDescending) = args.ToSortRequest(nameof(VariableLibraryDto.Name));
        await Cache.RevalidateAsync(
            PagedCacheKey(page, pageSize, _search, sortBy, sortDescending),
            () => Api.Variables.GetVariableLibrariesAsync(page: page, pageSize: pageSize, search: _search, projectId: ProjectId,
                sortBy: sortBy, sortDescending: sortDescending),
            ApplyLibrariesPage,
            loading => _loading = loading,
            () => InvokeAsync(StateHasChanged));
    }

    private bool TryApplyServerPage(LoadDataArgs args)
    {
        if (!IsServerScope) return false;
        _libraries = args.ToClientPage(_serverAll);
        _totalCount = args.ClientFilteredCount(_serverAll);
        return true;
    }

    private AetheusDataGrid<VariableLibraryDto>? _grid;
    private Task ReloadGrid() => _grid?.GoToPage(0) ?? Task.CompletedTask;

    private async Task ClearFilters()
    {
        _search = null;
        await ReloadGrid();
    }

    private void NewLibrary() => Nav.NavigateTo(IsProjectScope
        ? $"/variable-libraries/new?projectId={ProjectId}"
        : "/variable-libraries/new");

    private async Task StartHubAsync()
    {
        try
        {
            _hubConnection = HubFactory.Create("entities");
            _hubConnection.On<ResourceType, int, string>("EntityChanged", (type, _, _) =>
            {
                if (type != ResourceType.VariableLibrary) return Task.CompletedTask;
                return InvokeAsync(async () =>
                {
                    if (IsServerScope)
                    {
                        try { await LoadServerLibrariesAsync(); } catch (HttpRequestException) { }
                    }
                    await ReloadGrid();
                });
            });
            // Group membership is per-connection and lost on auto-reconnect - re-join + reload.
            _hubConnection.RejoinOnReconnect(() => InvokeAsync(async () =>
            {
                await _hubConnection.InvokeAsync("JoinEntityUpdates", ResourceType.VariableLibrary);
                if (IsServerScope)
                {
                    try { await LoadServerLibrariesAsync(); } catch (HttpRequestException) { }
                }
                await ReloadGrid();
            }));
            await _hubConnection.StartAsync();
            await _hubConnection.InvokeAsync("JoinEntityUpdates", ResourceType.VariableLibrary);
        }
        catch { /* Hub unavailable - degrade to static */ }
    }

    public async ValueTask DisposeAsync()
    {
        Permissions.OnPermissionsChanged -= OnPermissionsChanged;
        if (_hubConnection is not null)
        {
            await _hubConnection.LeaveEntityUpdatesAndDisposeAsync(ResourceType.VariableLibrary);
            _hubConnection = null;
        }
    }
}
