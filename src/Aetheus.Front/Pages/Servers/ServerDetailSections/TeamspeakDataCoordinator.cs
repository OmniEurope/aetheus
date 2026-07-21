// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;

namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

internal sealed class TeamspeakDataCoordinator(
    ApiClient api, TimeProvider? timeProvider = null, TimeSpan? refreshInterval = null) : IAsyncDisposable
{
    private readonly ApiClient _api = api;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan _refreshInterval = refreshInterval ?? TimeSpan.FromSeconds(30);
    private CancellationTokenSource? _lifetimeCts;
    private Task? _refreshLoop;
    private Func<Task>? _changed;
    private long _generation;
    private int _serverId;
    private PageQuery? _clientsQuery;
    private PageQuery? _channelsQuery;
    private PageQuery? _bansQuery;

    public TeamspeakDataDto State { get; private set; } = new();
    public List<TeamspeakClientDto> Clients { get; private set; } = [];
    public List<TeamspeakChannelDto> Channels { get; private set; } = [];
    public List<TeamspeakBanDto> Bans { get; private set; } = [];
    public int ClientsCount { get; private set; }
    public int ChannelsCount { get; private set; }
    public int BansCount { get; private set; }
    public bool ClientsLoading { get; private set; }
    public bool ChannelsLoading { get; private set; }
    public bool BansLoading { get; private set; }

    public async Task ResetAsync(int serverId, TeamspeakDataDto initialState, Func<Task> changed)
    {
        await StopRefreshLoopAsync().ConfigureAwait(false);
        _serverId = serverId;
        _changed = changed;
        _generation++;
        _clientsQuery = null;
        _channelsQuery = null;
        _bansQuery = null;
        State = initialState with { Clients = [], Channels = [], Bans = [] };
        Clients = [];
        Channels = [];
        Bans = [];
        ClientsCount = initialState.OnlineClients;
        ChannelsCount = initialState.ChannelCount;
        BansCount = 0;
        ClientsLoading = false;
        ChannelsLoading = false;
        BansLoading = false;

        _lifetimeCts = new CancellationTokenSource();
        await RefreshStateAsync(_generation, _lifetimeCts.Token).ConfigureAwait(false);
        _refreshLoop = RefreshLoopAsync(_generation, _lifetimeCts.Token);
    }

    public Task LoadClientsAsync(
        int page, int pageSize, string? search, string? sortBy, bool sortDescending)
    {
        _clientsQuery = new PageQuery(page, pageSize, search, sortBy, sortDescending);
        return LoadClientsCoreAsync(_clientsQuery, _generation, CurrentToken);
    }

    public Task LoadChannelsAsync(
        int page, int pageSize, string? search, string? sortBy, bool sortDescending)
    {
        _channelsQuery = new PageQuery(page, pageSize, search, sortBy, sortDescending);
        return LoadChannelsCoreAsync(_channelsQuery, _generation, CurrentToken);
    }

    public Task LoadBansAsync(
        int page, int pageSize, string? search, string? sortBy, bool sortDescending)
    {
        _bansQuery = new PageQuery(page, pageSize, search, sortBy, sortDescending);
        return LoadBansCoreAsync(_bansQuery, _generation, CurrentToken);
    }

    public async Task RefreshAllAsync()
    {
        var generation = _generation;
        var token = CurrentToken;
        await RefreshStateAsync(generation, token).ConfigureAwait(false);
        if (_clientsQuery is not null)
            await LoadClientsCoreAsync(_clientsQuery, generation, token).ConfigureAwait(false);
        if (_channelsQuery is not null)
            await LoadChannelsCoreAsync(_channelsQuery, generation, token).ConfigureAwait(false);
        if (_bansQuery is not null)
            await LoadBansCoreAsync(_bansQuery, generation, token).ConfigureAwait(false);
    }

    public async Task<List<TeamspeakChannelDto>> LoadAllChannelsAsync()
    {
        const int pageSize = 200;
        var result = new List<TeamspeakChannelDto>();
        for (var page = 1; ; page++)
        {
            var response = await _api.GetTeamspeakChannelsAsync(
                _serverId, page, pageSize, sortBy: "Name", ct: CurrentToken).ConfigureAwait(false);
            result.AddRange(response.Items);
            if (page * pageSize >= response.TotalCount || response.Items.Count == 0)
                return result;
        }
    }

    private async Task RefreshLoopAsync(long generation, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_refreshInterval, _timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    if (generation != _generation) return;
                    await RefreshAllAsync().ConfigureAwait(false);
                }
                catch (HttpRequestException)
                {
                    // Keep the last observable state; the next tick retries the real API.
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private async Task RefreshStateAsync(long generation, CancellationToken ct)
    {
        var state = await _api.GetTeamspeakStateAsync(_serverId, ct).ConfigureAwait(false);
        if (!IsCurrent(generation, ct)) return;
        State = state;
        await NotifyChangedAsync().ConfigureAwait(false);
    }

    private async Task LoadClientsCoreAsync(PageQuery query, long generation, CancellationToken ct)
    {
        ClientsLoading = true;
        await NotifyChangedAsync().ConfigureAwait(false);
        try
        {
            var result = await _api.GetTeamspeakClientsAsync(
                _serverId, query.Page, query.PageSize, query.Search,
                query.SortBy, query.SortDescending, ct).ConfigureAwait(false);
            if (!IsCurrent(generation, ct)) return;
            Clients = result.Items;
            ClientsCount = result.TotalCount;
        }
        finally
        {
            if (IsCurrent(generation, ct))
            {
                ClientsLoading = false;
                await NotifyChangedAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task LoadChannelsCoreAsync(PageQuery query, long generation, CancellationToken ct)
    {
        ChannelsLoading = true;
        await NotifyChangedAsync().ConfigureAwait(false);
        try
        {
            var result = await _api.GetTeamspeakChannelsAsync(
                _serverId, query.Page, query.PageSize, query.Search,
                query.SortBy, query.SortDescending, ct).ConfigureAwait(false);
            if (!IsCurrent(generation, ct)) return;
            Channels = result.Items;
            ChannelsCount = result.TotalCount;
        }
        finally
        {
            if (IsCurrent(generation, ct))
            {
                ChannelsLoading = false;
                await NotifyChangedAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task LoadBansCoreAsync(PageQuery query, long generation, CancellationToken ct)
    {
        BansLoading = true;
        await NotifyChangedAsync().ConfigureAwait(false);
        try
        {
            var result = await _api.GetTeamspeakBansAsync(
                _serverId, query.Page, query.PageSize, query.Search,
                query.SortBy, query.SortDescending, ct).ConfigureAwait(false);
            if (!IsCurrent(generation, ct)) return;
            Bans = result.Items;
            BansCount = result.TotalCount;
        }
        finally
        {
            if (IsCurrent(generation, ct))
            {
                BansLoading = false;
                await NotifyChangedAsync().ConfigureAwait(false);
            }
        }
    }

    private CancellationToken CurrentToken => _lifetimeCts?.Token ?? CancellationToken.None;
    private bool IsCurrent(long generation, CancellationToken ct) =>
        generation == _generation && !ct.IsCancellationRequested;
    private Task NotifyChangedAsync() => _changed?.Invoke() ?? Task.CompletedTask;

    private async Task StopRefreshLoopAsync()
    {
        if (_lifetimeCts is null) return;
        await _lifetimeCts.CancelAsync().ConfigureAwait(false);
        if (_refreshLoop is not null)
        {
            try { await _refreshLoop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _lifetimeCts.Dispose();
        _lifetimeCts = null;
        _refreshLoop = null;
    }

    public ValueTask DisposeAsync() => new(StopRefreshLoopAsync());

    private sealed record PageQuery(
        int Page, int PageSize, string? Search, string? SortBy, bool SortDescending);
}
