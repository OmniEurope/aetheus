// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Notifications;
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.AgentUpdate;

/// <summary>
/// Recette R2-034: tells the subscribers of the server's projects that its agent update ended
/// (<see cref="NotificationEventTypes.AgentUpdateCompleted"/> or
/// <see cref="NotificationEventTypes.AgentUpdateFailed"/>). The recipients are the subscribers of every
/// project the server is attached to; a server attached to no project notifies nobody. User deliveries
/// only, nothing goes to the admin channels.
/// </summary>
internal sealed class AgentUpdateNotificationPublisher(
    IUserNotificationService userNotifications,
    ILogger<AgentUpdateNotificationPublisher> logger)
{
    /// <summary>The event a terminal request is recorded as, or null while the update is still running.</summary>
    internal static string? EventTypeFor(AgentUpdateRequestStatus status) => status switch
    {
        AgentUpdateRequestStatus.Confirmed => NotificationEventTypes.AgentUpdateCompleted,
        AgentUpdateRequestStatus.Failed => NotificationEventTypes.AgentUpdateFailed,
        _ => null
    };

    public async Task PublishOutcomeAsync(AgentUpdateRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (EventTypeFor(request.Status) is not { } eventType) return;
        try
        {
            await userNotifications.RecordServerEventAsync(eventType, request.ServerId, projectId => new
            {
                AgentUpdateRequestId = request.Id,
                request.ServerId,
                ServerName = request.Server?.Name,
                ProjectId = projectId,
                FromVersion = request.ObservedVersion,
                ToVersion = request.TargetVersion,
                request.FailureCode,
                request.FailureDiagnostic
            }, ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Mandatory: the outcome is already saved, audited and broadcast by the caller (a heartbeat,
            // the coordinator, a progress report); a notification that cannot be recorded must not make
            // that caller fail.
            logger.LogWarning(exception,
                "[AgentUpdate] recording the {EventType} notification of server {ServerId} failed",
                eventType, request.ServerId);
        }
    }
}
