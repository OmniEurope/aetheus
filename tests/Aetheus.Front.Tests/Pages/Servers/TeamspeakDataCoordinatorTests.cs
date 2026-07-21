// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Servers.ServerDetailSections;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Microsoft.Extensions.Time.Testing;

namespace Aetheus.Front.Tests.Pages.Servers;

public sealed class TeamspeakDataCoordinatorTests
{
    [Fact]
    public async Task RefreshTimer_FetchesAndPublishesNewServerState()
    {
        var handler = new BunitTestHelper.TestHandler();
        handler.SetJsonResponse("api/servers/5/teamspeak",
            new TeamspeakDataDto { IsInstalled = true, ServerName = "Before" });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var time = new FakeTimeProvider();
        await using var coordinator = new TeamspeakDataCoordinator(
            new ApiClient(http), time, TimeSpan.FromSeconds(30));
        var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await coordinator.ResetAsync(
            5,
            new TeamspeakDataDto { IsInstalled = true, ServerName = "Initial" },
            () =>
            {
                if (coordinator.State.ServerName == "After") refreshed.TrySetResult();
                return Task.CompletedTask;
            });
        handler.SetJsonResponse("api/servers/5/teamspeak",
            new TeamspeakDataDto { IsInstalled = true, ServerName = "After" });

        time.Advance(TimeSpan.FromSeconds(30));
        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(1), Xunit.TestContext.Current.CancellationToken);

        Assert.Equal("After", coordinator.State.ServerName);
        Assert.True(handler.Requests.Count(request => request.Url.Contains("/teamspeak", StringComparison.Ordinal)) >= 2);
    }

    [Fact]
    public async Task ResetAsync_ClearsPreviousServerCollections()
    {
        var handler = new BunitTestHelper.TestHandler();
        handler.SetJsonResponse("api/servers/5/teamspeak", new TeamspeakDataDto { IsInstalled = true });
        handler.SetPaginatedJsonResponse("api/servers/5/teamspeak/clients",
            new[] { new TeamspeakClientDto { ClientId = 1, Nickname = "Old" } });
        handler.SetJsonResponse("api/servers/6/teamspeak", new TeamspeakDataDto { IsInstalled = true });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        await using var coordinator = new TeamspeakDataCoordinator(new ApiClient(http));

        await coordinator.ResetAsync(5, new TeamspeakDataDto(), () => Task.CompletedTask);
        await coordinator.LoadClientsAsync(1, 25, null, "Nickname", false);
        Assert.Single(coordinator.Clients);

        await coordinator.ResetAsync(6, new TeamspeakDataDto(), () => Task.CompletedTask);

        Assert.Empty(coordinator.Clients);
        Assert.Equal(0, coordinator.ClientsCount);
    }
}
