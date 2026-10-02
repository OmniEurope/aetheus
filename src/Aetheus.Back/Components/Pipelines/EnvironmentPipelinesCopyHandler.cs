// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Environments;
using Aetheus.Back.Services.DomainEvents;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// Copies an environment's pipelines into the git repository of the project it is linked to. Moved
/// out of <c>EnvironmentService</c> unchanged: best effort, published in the background (a clone can
/// be slow) so saving the environment returns at once, and a failure is logged, never reported to the
/// caller whose save already succeeded.
/// </summary>
internal sealed class EnvironmentPipelinesCopyHandler(
    IPipelineGitService git,
    ILogger<EnvironmentPipelinesCopyHandler> logger)
    : IDomainEventHandler<EnvironmentLinkedToProjectEvent>
{
    public async Task HandleAsync(EnvironmentLinkedToProjectEvent domainEvent, CancellationToken ct = default)
    {
        try
        {
            var copied = await git.CopyEnvironmentPipelinesToProjectAsync(
                domainEvent.EnvironmentId, domainEvent.EnvironmentName, domainEvent.ProjectId, "system", ct).ConfigureAwait(false);
            if (copied > 0)
                logger.LogInformation("Copied {Count} environment '{Env}' pipeline(s) into project {ProjectId} git.",
                    copied, domainEvent.EnvironmentName, domainEvent.ProjectId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Background pipeline copy failed for env '{Env}' → project {ProjectId}.",
                domainEvent.EnvironmentName, domainEvent.ProjectId);
        }
    }
}
