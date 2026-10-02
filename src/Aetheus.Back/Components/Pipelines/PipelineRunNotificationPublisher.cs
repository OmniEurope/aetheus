// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using Aetheus.Back.Components.Notifications;
using Aetheus.Back.Components.Pipelines.Events;
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// Records pipeline outcomes (<c>pipeline.failed</c>, <c>pipeline.succeeded</c>,
/// <c>pipeline.approval-requested</c>) as per-user deliveries for the subscribers of the owning project.
///
/// Deliberately user deliveries ONLY: this goes straight to
/// <see cref="IUserNotificationService.RecordProjectEventAsync"/> and never through
/// <see cref="INotificationService.SendEventAsync"/>, so these new events feed neither the admin channel
/// rules (Slack, Teams, Webhook, Email) nor the AI task triggers. Nothing reaches an external channel;
/// wiring them there is left to an explicit user decision. A legacy pipeline with no resolvable project
/// carries a null project and records nothing.
/// </summary>
public sealed class PipelineRunNotificationPublisher(
    IPipelineRepository repo,
    IUserNotificationService userNotifications)
{
    /// <summary>The event type a terminal status is recorded as, or null for a status that is not recorded.</summary>
    public static string? EventTypeFor(PipelineStatus status) => status switch
    {
        PipelineStatus.Failed => NotificationEventTypes.PipelineFailed,
        PipelineStatus.Success => NotificationEventTypes.PipelineSucceeded,
        _ => null
    };

    public async Task PublishRunCompletedAsync(int pipelineRunId, PipelineStatus status, CancellationToken ct = default)
    {
        if (EventTypeFor(status) is not { } eventType) return;
        await RecordRunEventAsync(pipelineRunId, eventType, (run, projectId) => new
        {
            PipelineRunId = run.Id,
            run.PipelineId,
            PipelineName = run.Pipeline!.Name,
            run.BuildNumber,
            ProjectId = projectId,
            Status = status.ToString()
        }, ct).ConfigureAwait(false);
    }

    public async Task PublishApprovalRequestedAsync(PipelineApprovalRequestedEvent domainEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        await RecordRunEventAsync(domainEvent.PipelineRunId, NotificationEventTypes.PipelineApprovalRequested, (run, projectId) => new
        {
            PipelineRunId = run.Id,
            run.PipelineId,
            PipelineName = run.Pipeline!.Name,
            run.BuildNumber,
            ProjectId = projectId,
            domainEvent.StageName,
            domainEvent.EnvironmentName
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Recette R-522: a scheduled, pushed or chained launch the configuration checks refused leaves no
    /// run behind (the nightly was refused every night and the pipeline's page showed nothing). The
    /// subscribers of the project are told, with the trigger and the reasons the launcher gave.
    /// </summary>
    public async Task PublishLaunchRefusedAsync(
        Pipeline pipeline, string triggerSource, string reason, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        var projectId = await repo.GetPipelineProjectIdAsync(pipeline, ct).ConfigureAwait(false);
        await userNotifications.RecordProjectEventAsync(
            NotificationEventTypes.PipelineLaunchRefused,
            JsonSerializer.Serialize(new
            {
                PipelineId = pipeline.Id,
                PipelineName = pipeline.Name,
                ProjectId = projectId,
                TriggerSource = triggerSource,
                Reason = reason
            }),
            ct).ConfigureAwait(false);
    }

    // Shared by both events: a run whose pipeline cannot be loaded records nothing.
    private async Task RecordRunEventAsync(
        int pipelineRunId, string eventType, Func<PipelineRun, int?, object> buildPayload, CancellationToken ct)
    {
        var run = await repo.GetPipelineRunWithPipelineAsync(pipelineRunId, ct).ConfigureAwait(false);
        if (run?.Pipeline is null) return;
        var projectId = await repo.GetPipelineProjectIdAsync(run.Pipeline, ct).ConfigureAwait(false);
        await userNotifications.RecordProjectEventAsync(eventType, JsonSerializer.Serialize(buildPayload(run, projectId)), ct)
            .ConfigureAwait(false);
    }
}
