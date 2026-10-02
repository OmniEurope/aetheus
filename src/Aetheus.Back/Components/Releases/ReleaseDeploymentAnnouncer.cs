// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Components.Tasks;
using Aetheus.Back.Services.DomainEvents;

namespace Aetheus.Back.Components.Releases;

/// <summary>Announces a release that a pipeline's <c>type: release</c> step recorded as deployed.</summary>
public interface IReleaseDeploymentAnnouncer
{
    /// <summary>Raises <see cref="ReleaseDeployedEvent"/> for the release, naming the stage the agent
    /// is running in that run so the target environment's deployment policy applies.</summary>
    Task AnnounceAsync(int releaseId, int pipelineRunId, int agentServerId, CancellationToken ct = default);
}

/// <summary>
/// Recette R-520: a release goes live in two ways. A <c>type: deploy</c> step raises
/// <see cref="ReleaseDeployedEvent"/> when its task closes; a <c>type: release</c> step with
/// <c>deployed: true</c> (how <c>aetheus-deploy-prod</c> records its release) raised nothing, so what
/// subscribes to a deployment never heard of that one. The stage is the one whose step the calling
/// agent is running: the request carries the run, not the step. Recette R2-001: advancing a branch no
/// longer subscribes to this event; it is the explicit <c>type: advance-branch</c> step.
/// </summary>
public sealed class ReleaseDeploymentAnnouncer(
    IPipelineRepository pipelines,
    IDomainEventDispatcher domainEvents) : IReleaseDeploymentAnnouncer
{
    public async Task AnnounceAsync(int releaseId, int pipelineRunId, int agentServerId, CancellationToken ct = default)
    {
        var stageName = await pipelines.FindRunningStageNameAsync(pipelineRunId, agentServerId, ct).ConfigureAwait(false);
        await domainEvents.DispatchAsync(
            new ReleaseDeployedEvent(releaseId, pipelineRunId, stageName, IsRollback: false), ct).ConfigureAwait(false);
    }
}
