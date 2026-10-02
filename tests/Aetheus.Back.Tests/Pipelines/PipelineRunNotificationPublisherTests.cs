// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Notifications;
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Components.Pipelines.Events;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Services.DomainEvents;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Aetheus.Back.Tests;

public sealed class PipelineRunNotificationPublisherTests
{
    private readonly IPipelineRepository _repo = Substitute.For<IPipelineRepository>();
    private readonly IUserNotificationService _userNotifications = Substitute.For<IUserNotificationService>();
    private readonly PipelineRunNotificationPublisher _sut;

    public PipelineRunNotificationPublisherTests()
    {
        _sut = new PipelineRunNotificationPublisher(_repo, _userNotifications);
        var pipeline = new Pipeline { Id = 4, Name = "build", EnvironmentId = 2 };
        _repo.GetPipelineRunWithPipelineAsync(9, Arg.Any<CancellationToken>())
            .Returns(new PipelineRun { Id = 9, PipelineId = 4, Pipeline = pipeline, BuildNumber = 12 });
        _repo.GetPipelineProjectIdAsync(pipeline, Arg.Any<CancellationToken>()).Returns(10);
    }

    [Theory]
    [InlineData(PipelineStatus.Failed, NotificationEventTypes.PipelineFailed)]
    [InlineData(PipelineStatus.Success, NotificationEventTypes.PipelineSucceeded)]
    public async Task TerminalRun_RecordsItsEvent_WithTheOwningProject(PipelineStatus status, string expectedType)
    {
        await _sut.PublishRunCompletedAsync(9, status, TestContext.Current.CancellationToken);

        await _userNotifications.Received(1).RecordProjectEventAsync(
            expectedType,
            Arg.Is<string>(json => UserNotificationService.ReadProjectId(json) == 10),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(PipelineStatus.Cancelled)]
    [InlineData(PipelineStatus.Partial)]
    [InlineData(PipelineStatus.RolledBack)]
    public async Task OtherStatuses_RecordNothing(PipelineStatus status)
    {
        await _sut.PublishRunCompletedAsync(9, status, TestContext.Current.CancellationToken);

        await _userNotifications.DidNotReceiveWithAnyArgs()
            .RecordProjectEventAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ApprovalRequested_RecordsTheApprovalEvent_WithTheOwningProject()
    {
        await _sut.PublishApprovalRequestedAsync(
            new PipelineApprovalRequestedEvent(9, "deploy", "prod"), TestContext.Current.CancellationToken);

        await _userNotifications.Received(1).RecordProjectEventAsync(
            NotificationEventTypes.PipelineApprovalRequested,
            Arg.Is<string>(json => UserNotificationService.ReadProjectId(json) == 10),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The bridge records user deliveries only: resolved from a container where the admin notification
    /// service is available, none of the three pipeline events ever reaches SendEventAsync (admin channel
    /// rules and AI triggers).
    /// </summary>
    [Fact]
    public async Task PipelineEvents_NeverReachSendEventAsync()
    {
        var notifications = Substitute.For<INotificationService>();
        await using var provider = new ServiceCollection()
            .AddSingleton(_repo)
            .AddSingleton(_userNotifications)
            .AddSingleton(notifications)
            .AddScoped<PipelineRunNotificationPublisher>()
            .BuildServiceProvider();
        var publisher = provider.GetRequiredService<PipelineRunNotificationPublisher>();

        await publisher.PublishRunCompletedAsync(9, PipelineStatus.Failed, TestContext.Current.CancellationToken);
        await publisher.PublishRunCompletedAsync(9, PipelineStatus.Success, TestContext.Current.CancellationToken);
        await publisher.PublishApprovalRequestedAsync(
            new PipelineApprovalRequestedEvent(9, "deploy", "prod"), TestContext.Current.CancellationToken);

        await _userNotifications.ReceivedWithAnyArgs(3)
            .RecordProjectEventAsync(default!, default!, TestContext.Current.CancellationToken);
        await notifications.DidNotReceiveWithAnyArgs()
            .SendEventAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CompletedHandler_QueuesOnlyRecordedStatuses()
    {
        var queue = Substitute.For<IBackgroundTaskQueue>();
        var handler = new PipelineRunCompletedNotificationHandler(queue);

        await handler.HandleAsync(new PipelineRunCompletedEvent(9, PipelineStatus.Failed), TestContext.Current.CancellationToken);
        await handler.HandleAsync(new PipelineRunCompletedEvent(9, PipelineStatus.Cancelled), TestContext.Current.CancellationToken);

        queue.Received(1).Enqueue(Arg.Any<Func<IServiceProvider, CancellationToken, Task>>());
    }
}
