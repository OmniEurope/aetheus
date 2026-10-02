// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Notifications;
using Aetheus.Back.Services.DomainEvents;

namespace Aetheus.Back.Components.Tasks;

/// <summary>
/// Recette R2-034: a release now live is notified to the subscribers of its project
/// (<see cref="NotificationEventTypes.ReleaseDeployed"/>). It hangs off <see cref="ReleaseDeployedEvent"/>,
/// which both ways a release goes live raise: a deploy task's closure and a <c>type: release</c> step
/// with <c>deployed: true</c>. User deliveries only, like the pipeline outcomes: nothing is sent to the
/// admin channels. Dispatched non-strictly, so a failure here never turns a deployment into a failure.
/// </summary>
public sealed class ReleaseDeployedNotificationHandler(IUserNotificationService userNotifications)
    : IDomainEventHandler<ReleaseDeployedEvent>
{
    public Task HandleAsync(ReleaseDeployedEvent domainEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return userNotifications.RecordReleaseDeployedAsync(
            domainEvent.ReleaseId, domainEvent.PipelineRunId, domainEvent.StageName, domainEvent.IsRollback, ct);
    }
}
