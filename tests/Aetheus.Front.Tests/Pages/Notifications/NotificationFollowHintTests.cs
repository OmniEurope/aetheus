// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Components.Notifications;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Notifications;

/// <summary>
/// Recette R2-034: a user who follows no project receives nothing. The bell and the preferences say why
/// and offer to follow every project the user can read; the preferences list the new events.
/// </summary>
public sealed class NotificationFollowHintTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;
    private List<ProjectSubscriptionDto> _subscriptions = [];

    public NotificationFollowHintTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        _handler.SetAsyncJsonResponse(HttpMethod.Get, "api/notifications/me/unread-count", _ => Task.FromResult(0));
        _handler.SetPaginatedJsonResponse(HttpMethod.Get, "api/notifications/me?", Array.Empty<NotificationDeliveryDto>());
        _handler.SetAsyncJsonResponse(HttpMethod.Get, "api/notifications/me/subscriptions", _ => Task.FromResult(_subscriptions));
        _handler.SetJsonResponse(HttpMethod.Get, "api/notifications/me/preferences", new List<NotificationPreferenceDto>
        {
            new() { EventType = NotificationEventTypes.ReleaseDeployed, IsEnabled = true, DefaultEnabled = true, CarriesProjectId = true },
            new() { EventType = NotificationEventTypes.AgentUpdateCompleted, IsEnabled = false, CarriesProjectId = true },
            new() { EventType = NotificationEventTypes.AgentUpdateFailed, IsEnabled = true, DefaultEnabled = true, CarriesProjectId = true }
        });
        _handler.SetAsyncJsonResponse(HttpMethod.Post, "api/notifications/me/subscriptions/all", _ =>
        {
            _subscriptions = [new ProjectSubscriptionDto { ProjectId = 4, ProjectName = "Shop" }];
            return Task.FromResult(_subscriptions);
        });
    }

    [Fact]
    public void Bell_FollowingNothing_ShowsTheHint_AndFollowAllFollowsTheReadableProjects()
    {
        var bell = Render<NotificationBell>();

        bell.Find(".notification-bell-btn").Click();

        bell.WaitForAssertion(() => Assert.Contains("NotificationNoSubscriptionHint", bell.Find(".notification-bell-no-subscription").TextContent, StringComparison.Ordinal));
        Assert.Empty(bell.FindAll(".task-tracker-empty"));

        bell.Find(".notification-bell-follow-all").Click();

        bell.WaitForAssertion(() => Assert.Empty(bell.FindAll(".notification-bell-no-subscription")));
        Assert.Contains(_handler.Requests, request => request.Method == "POST"
            && request.Url.EndsWith("api/notifications/me/subscriptions/all", StringComparison.Ordinal));
    }

    [Fact]
    public void Bell_FollowingAProject_ShowsNoHint()
    {
        _subscriptions = [new ProjectSubscriptionDto { ProjectId = 4, ProjectName = "Shop" }];
        var bell = Render<NotificationBell>();

        bell.Find(".notification-bell-btn").Click();

        bell.WaitForAssertion(() => Assert.Contains(_handler.Requests, request => request.Url.EndsWith("api/notifications/me/subscriptions", StringComparison.Ordinal)));
        bell.WaitForAssertion(() => Assert.NotEmpty(bell.FindAll(".task-tracker-empty")));
        Assert.Empty(bell.FindAll(".notification-bell-no-subscription"));
    }

    [Fact]
    public void Preferences_FollowingNothing_ExplainWhy_AndFollowAllListsTheProjects()
    {
        var panel = Render<NotificationPreferencesPanel>();

        panel.WaitForAssertion(() => Assert.Contains("NotificationNoSubscriptionHint", panel.Find(".notification-subscriptions-empty").TextContent, StringComparison.Ordinal));

        panel.Find(".notification-follow-all").Click();

        panel.WaitForAssertion(() => Assert.Single(panel.FindAll(".notification-subscription")));
        Assert.Empty(panel.FindAll(".notification-subscriptions-empty"));
        Assert.Contains("Shop", panel.Find(".notification-subscription").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Preferences_ListTheReleaseAndAgentUpdateEvents()
    {
        var panel = Render<NotificationPreferencesPanel>();

        panel.WaitForAssertion(() => Assert.Equal(3, panel.FindAll(".notification-preference-toggle").Count));
        Assert.Contains("NotificationEvent_release_deployed", panel.Markup, StringComparison.Ordinal);
        Assert.Contains("NotificationEvent_agent_update_completed", panel.Markup, StringComparison.Ordinal);
        Assert.Contains("NotificationEvent_agent_update_failed", panel.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(NotificationEventTypes.ReleaseDeployed, "Releases")]
    [InlineData(NotificationEventTypes.AgentUpdateCompleted, "Servers")]
    [InlineData(NotificationEventTypes.AgentUpdateFailed, "Servers")]
    public void NewEvents_AreGroupedWithWhatRaisesThem(string eventType, string group) =>
        Assert.Equal(group, NotificationPreferencesPanel.GroupKey(eventType));

    [Theory]
    [InlineData(NotificationEventTypes.ReleaseDeployed, OmniIconName.CloudArrowUp)]
    [InlineData(NotificationEventTypes.AgentUpdateCompleted, OmniIconName.Upgrade)]
    [InlineData(NotificationEventTypes.AgentUpdateFailed, OmniIconName.Error)]
    [InlineData(NotificationEventTypes.PipelineApprovalRequested, OmniIconName.Stamp)]
    [InlineData("pipeline.other", OmniIconName.RocketLaunch)]
    [InlineData("analysis.gate.blocked", OmniIconName.ShieldWarning)]
    [InlineData("analysis.report.completed", OmniIconName.ShieldCheck)]
    [InlineData("custom.unknown", OmniIconName.Bell)]
    public void EventIcon_ExactTypeFirst_ThenTheMostSpecificFamily(string eventType, OmniIconName expected) =>
        Assert.Equal(expected, NotificationLabels.EventIcon(eventType));
}
