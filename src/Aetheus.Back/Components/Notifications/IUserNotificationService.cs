// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Notifications;

/// <summary>
/// Notifications as the current user sees them: the deliveries addressed to them, their per-event
/// preferences and the projects they follow, plus the recording of a project event for its subscribers.
/// Every read and write is scoped to the user id the caller passes; nothing here crosses users.
/// </summary>
public interface IUserNotificationService
{
    Task<PaginatedResult<NotificationDeliveryDto>> GetDeliveriesAsync(
        int userId, UserNotificationPageRequest request, CancellationToken ct = default);
    Task<int> GetUnreadCountAsync(int userId, CancellationToken ct = default);

    /// <summary>Recette R-224: the values the user's /notifications grid filters offer.</summary>
    Task<UserNotificationFilterValuesDto> GetFilterValuesAsync(int userId, CancellationToken ct = default);

    /// <summary>Marks one of the user's deliveries as read; false when it is not theirs or does not exist.</summary>
    Task<bool> MarkReadAsync(int userId, int deliveryId, CancellationToken ct = default);

    /// <summary>Marks every unread delivery of the user as read and returns how many changed.</summary>
    Task<int> MarkAllReadAsync(int userId, CancellationToken ct = default);

    Task<List<NotificationPreferenceDto>> GetPreferencesAsync(int userId, CancellationToken ct = default);
    Task<List<NotificationPreferenceDto>> UpdatePreferencesAsync(
        int userId, UpdateNotificationPreferencesRequest request, CancellationToken ct = default);

    Task<List<ProjectSubscriptionDto>> GetSubscriptionsAsync(int userId, CancellationToken ct = default);

    /// <summary>Subscribes the user to the project (idempotent); null when the project does not exist.</summary>
    Task<ProjectSubscriptionDto?> SubscribeAsync(int userId, int projectId, CancellationToken ct = default);

    Task<bool> UnsubscribeAsync(int userId, int projectId, CancellationToken ct = default);

    /// <summary>
    /// Recette R2-034, "follow all my projects": subscribes the user to every project of
    /// <paramref name="readableProjectIds"/> not followed yet, null meaning every project (a wildcard
    /// read grant). Returns all of the user's subscriptions afterwards.
    /// </summary>
    Task<List<ProjectSubscriptionDto>> FollowProjectsAsync(
        int userId, IReadOnlyCollection<int>? readableProjectIds, CancellationToken ct = default);

    /// <summary>
    /// Recette R2-034: records <see cref="NotificationEventTypes.ReleaseDeployed"/> for the subscribers of
    /// the release's project. Returns the number of deliveries written; zero for an unknown release.
    /// </summary>
    Task<int> RecordReleaseDeployedAsync(
        int releaseId, int? pipelineRunId, string? stageName, bool isRollback, CancellationToken ct = default);

    /// <summary>
    /// Recette R2-034: records a server event once per project the server is attached to, with that
    /// project's id in the payload built by <paramref name="payloadForProject"/>. A server attached to no
    /// project records nothing. Returns the number of deliveries written.
    /// </summary>
    Task<int> RecordServerEventAsync(
        string eventType, int serverId, Func<int, object> payloadForProject, CancellationToken ct = default);

    /// <summary>
    /// Audit R2-023 follow-up: writes one delivery per active administrator, whatever their project
    /// subscriptions and preferences, for an event that concerns the platform rather than a project (a
    /// server attached to no project has no subscriber to tell). Returns the number of rows written.
    /// </summary>
    Task<int> RecordAdministratorEventAsync(
        string eventType, string subject, string jsonPayload, CancellationToken ct = default);

    /// <summary>
    /// Writes one delivery per subscriber of the event's project whose preferences accept it and who can
    /// still read the project. Returns the number of rows written; zero when the payload names no project.
    /// No transport exists for these deliveries, so they are recorded as NotConfigured and never Sent.
    /// </summary>
    Task<int> RecordProjectEventAsync(string eventType, string jsonPayload, CancellationToken ct = default);
}
