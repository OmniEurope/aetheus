// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines.Events;
using Aetheus.Back.Services.DomainEvents;

namespace Aetheus.Back.Components.Releases;

/// <summary>
/// Bridges the in-process domain event onto the existing release notification API.
/// Replaces the prior service-locator dependency from <c>PipelineRunService</c>.
/// </summary>
public class PipelineRunCompletedReleaseHandler(IReleaseService releaseService)
    : IDomainEventHandler<PipelineRunCompletedEvent>
{
    public Task HandleAsync(PipelineRunCompletedEvent domainEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return releaseService.NotifyPipelineRunCompletedAsync(domainEvent.PipelineRunId, domainEvent.Status, ct);
    }
}
