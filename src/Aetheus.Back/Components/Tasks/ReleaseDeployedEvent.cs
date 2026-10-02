// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Services.DomainEvents;

namespace Aetheus.Back.Components.Tasks;

/// <summary>
/// Raised once a deploy task has passed its health gate and the release it carried is recorded as
/// <c>Deployed</c>. This is the single authoritative "a release is now live" moment, which is why
/// consequences of a deployment (as opposed to conditions for it) hang off this event.
/// </summary>
/// <param name="ReleaseId">The release now serving.</param>
/// <param name="PipelineRunId">The run that deployed it, when the deploy came from a pipeline.</param>
/// <param name="StageName">The stage the deploy step belonged to; it names the target environment in
/// the run's YAML, which is where per-environment deployment policy is read from.</param>
/// <param name="IsRollback">True when this deployment reinstated a previous release.</param>
public sealed record ReleaseDeployedEvent(
    int ReleaseId,
    int? PipelineRunId,
    string? StageName,
    bool IsRollback) : IDomainEvent;
