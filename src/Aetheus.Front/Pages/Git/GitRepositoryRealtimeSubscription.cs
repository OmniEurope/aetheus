// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Git;

internal sealed class GitRepositoryRealtimeSubscription(HubConnectionFactory hubFactory)
    : IAsyncDisposable
{
    private HubConnection? _hub;
    private int _repositoryId;
    private IReadOnlyList<Func<Task>> _reloads = [];

    public async Task StartAsync(
        int repositoryId,
        Func<Task> branchesChanged,
        Func<Task> tagsChanged,
        Func<Task> commitsChanged,
        Func<Task> pullRequestsChanged,
        Func<Task> repositoryChanged)
    {
        await DisposeAsync();
        _repositoryId = repositoryId;
        _reloads =
        [
            branchesChanged,
            tagsChanged,
            commitsChanged,
            pullRequestsChanged,
            repositoryChanged
        ];
        var hub = hubFactory.Create("git");
        _hub = hub;
        hub.On<int>("BranchesChanged", id => InvokeForRepositoryAsync(id, branchesChanged));
        hub.On<int>("TagsChanged", id => InvokeForRepositoryAsync(id, tagsChanged));
        hub.On<int>("CommitsChanged", id => InvokeForRepositoryAsync(id, commitsChanged));
        hub.On<int>("PullRequestsChanged", id => InvokeForRepositoryAsync(id, pullRequestsChanged));
        hub.On<int>("RepositoryChanged", id => InvokeForRepositoryAsync(id, repositoryChanged));
        hub.RejoinOnReconnect(RejoinAndReloadAsync);
        try
        {
            await hub.StartAsync();
            await hub.InvokeAsync("JoinRepositoryGroup", repositoryId);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            await hub.DisposeAsync();
            if (ReferenceEquals(_hub, hub)) _hub = null;
        }
    }

    private Task InvokeForRepositoryAsync(int repositoryId, Func<Task> callback) =>
        repositoryId == _repositoryId ? callback() : Task.CompletedTask;

    private async Task RejoinAndReloadAsync()
    {
        var hub = _hub;
        if (hub is null) return;
        await hub.InvokeAsync("JoinRepositoryGroup", _repositoryId);
        await Task.WhenAll(_reloads.Select(reload => reload()));
    }

    public async ValueTask DisposeAsync()
    {
        var hub = _hub;
        _hub = null;
        if (hub is null) return;
        try
        {
            if (hub.State == HubConnectionState.Connected)
                await hub.InvokeAsync("LeaveRepositoryGroup", _repositoryId);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException) { }
        await hub.DisposeAsync();
        _reloads = [];
    }
}
