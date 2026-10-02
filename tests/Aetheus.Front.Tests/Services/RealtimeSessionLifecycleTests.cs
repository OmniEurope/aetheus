// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Aetheus.Front.Tests.Services;

public class RealtimeSessionLifecycleTests
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

    [Fact]
    public async Task StopAsync_ClearsPreviousUserState_AndAllowsFreshSameRuntimeStart()
    {
        var auth = new AuthStateProvider(new FakeJsRuntime(), NullLogger<AuthStateProvider>.Instance);
        typeof(AuthStateProvider).GetProperty(nameof(AuthStateProvider.Token))!.SetValue(auth, "user-a-token");
        typeof(AuthStateProvider).GetProperty(nameof(AuthStateProvider.Roles))!
            .SetValue(auth, new List<string> { "Admin" });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ApiBaseUrl"] = "http://test" })
            .Build();
        var factory = new CountingHubConnectionFactory(config, auth);
        var api = new ApiClient(new HttpClient(new UnexpectedRequestHandler())
        {
            BaseAddress = new Uri("http://test/")
        });
        var permissions = new PermissionService();
        var tracker = new TaskTrackerService(api, auth, factory, NullLogger<TaskTrackerService>.Instance);
        var alerts = new AlertNotificationService(auth, factory);
        var userNotifications = new UserNotificationService(
            factory, auth, api, permissions, new FakeTimeProvider());
        var lifecycle = new RealtimeSessionLifecycle(factory, tracker, alerts, userNotifications);

        await tracker.StartAsync(Xunit.TestContext.Current.CancellationToken);
        await alerts.StartAsync();
        await userNotifications.StartAsync();

        var trackerHubA = GetPrivate(tracker, "_hub");
        var alertHubA = GetPrivate(alerts, "_hub");
        var userHubA = GetPrivate(userNotifications, "_hub");
        Assert.NotNull(trackerHubA);
        Assert.NotNull(alertHubA);
        Assert.NotNull(userHubA);

        var tasks = (Dictionary<int, ServerTaskDto>)GetPrivate(tracker, "_byId")!;
        tasks[1] = new ServerTaskDto
        {
            Id = 1,
            ServerId = 10,
            ServerName = "user-a-server",
            Name = "User A deploy",
            Status = TaskExecutionStatus.Running,
            CreatedAt = new DateTime(2026, 7, 20)
        };
        var recent = (List<AlertTriggeredDto>)GetPrivate(alerts, "_recent")!;
        recent.Add(new AlertTriggeredDto { RuleName = "User A alert", TriggeredAt = new DateTime(2026, 7, 20) });
        typeof(AlertNotificationService).GetField("_unreadCount", Priv)!.SetValue(alerts, 1);
        userNotifications.HandleServerMessage("UserARoles");

        await lifecycle.StopAsync();

        Assert.Equal(1, factory.StopAllCount);
        Assert.Null(GetPrivate(tracker, "_hub"));
        Assert.Null(GetPrivate(tracker, "_startRetryCts"));
        Assert.Empty(tracker.Tasks);
        Assert.Null(GetPrivate(alerts, "_hub"));
        Assert.Empty(alerts.Recent);
        Assert.Equal(0, alerts.UnreadCount);
        Assert.Null(GetPrivate(userNotifications, "_hub"));
        Assert.Null(GetPrivate(userNotifications, "_startRetryCts"));
        Assert.Null(GetPrivate(userNotifications, "_debounce"));

        typeof(AuthStateProvider).GetProperty(nameof(AuthStateProvider.Token))!.SetValue(auth, "user-b-token");
        await tracker.StartAsync(Xunit.TestContext.Current.CancellationToken);
        await alerts.StartAsync();
        await userNotifications.StartAsync();

        Assert.NotSame(trackerHubA, GetPrivate(tracker, "_hub"));
        Assert.NotSame(alertHubA, GetPrivate(alerts, "_hub"));
        Assert.NotSame(userHubA, GetPrivate(userNotifications, "_hub"));
        Assert.Empty(tracker.Tasks);
        Assert.Empty(alerts.Recent);

        await lifecycle.StopAsync();
    }

    private static object? GetPrivate(object owner, string field) =>
        owner.GetType().GetField(field, Priv)!.GetValue(owner);

    private sealed class UnexpectedRequestHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(
                new InvalidOperationException($"Unexpected API request: {request.RequestUri}"));
    }
}
