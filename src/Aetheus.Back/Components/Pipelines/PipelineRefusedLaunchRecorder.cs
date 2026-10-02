// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// Recette R-522: an automated launch (schedule, push, webhook) that is refused has nobody in front of it
/// to read the refusal, and a refusal happens before any run exists: the nightly was refused every night
/// and the pipeline's page showed nothing. The refusal is recorded as a run that failed at once and
/// carries the reason, so it shows in the pipeline's own run list like any other failure, and the
/// subscribers of the project are told.
/// </summary>
/// <remarks>
/// Inside a caller's transaction nothing is recorded: the caller's rollback would erase the run after
/// its events had already gone out, and the caller records the refusal once its transaction is over
/// (the webhook does). Once the run is saved, the audit line, the live event and the notification are
/// best effort, so a failure of one of them never replaces the refusal the caller rethrows.
/// </remarks>
public sealed class PipelineRefusedLaunchRecorder(
    IPipelineRepository repo,
    IHubContext<PipelineHub> pipelineHub,
    IAuditService audit,
    PipelineRunNotificationPublisher notifications,
    TimeProvider timeProvider,
    ILogger<PipelineRefusedLaunchRecorder> logger,
    IDbTransactionScope? transaction = null)
{
    /// <summary>
    /// Records the refusal. <paramref name="preparation"/> is null when the refusal came from the
    /// preparation itself (an invalid definition, a source repository that could not be read): the run
    /// then carries no snapshot, only the branch the trigger named and the reason.
    /// </summary>
    public async Task RecordAsync(
        Pipeline pipeline,
        PipelineRunPreparation? preparation,
        string triggerSource,
        string reason,
        IReadOnlyDictionary<string, string>? additionalVariables,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        if (transaction?.InTransaction == true) return;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var run = new PipelineRun
        {
            PipelineId = pipeline.Id,
            Status = PipelineStatus.Failed,
            StartedAt = now,
            CompletedAt = now,
            YamlSnapshot = preparation?.YamlSnapshot,
            BranchName = preparation?.BranchName ?? NamedBranch(additionalVariables),
            CommitHash = preparation?.CommitHash ?? PipelineRunService.ResolveSourceCommit(additionalVariables),
            RepositoryUrl = preparation?.RepositoryUrl,
            WarningsJson = JsonSerializer.Serialize(new[] { BuildReason(triggerSource, reason) })
        };
        // A refused launch takes a build number like any run: numbers never repeat, and are not gapless.
        run.BuildNumber = await repo.ReserveNextBuildNumberAsync(pipeline.Id, ct).ConfigureAwait(false);
        await repo.AddPipelineRunAsync(run, ct).ConfigureAwait(false);
        await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        await TellAsync(run.Id, "audit", () => audit.LogAsync(
            "LaunchRefused", "PipelineRun", run.Id, $"{pipeline.Name} | {triggerSource} | {reason}", ct)).ConfigureAwait(false);
        await TellAsync(run.Id, "live event", () => pipelineHub.Clients
            .Groups(HubGroups.PipelineRunUpdates(run.Id, pipeline.Id))
            .SendAsync("PipelineRunCompleted", run.Id, PipelineStatus.Failed, ct)).ConfigureAwait(false);
        await TellAsync(run.Id, "notification", () =>
            notifications.PublishLaunchRefusedAsync(pipeline, triggerSource, reason, ct)).ConfigureAwait(false);
    }

    private async Task TellAsync(int runId, string what, Func<Task> effect)
    {
        try
        {
            await effect().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Mandatory catch: the run carrying the refusal is saved, and the caller rethrows the
            // refusal itself; a failed side effect must not take its place (the scheduler catches only
            // refusals, so any other exception would stop the pipelines scheduled after this one).
            logger.LogWarning(ex, "The {Effect} of refused launch run {RunId} could not be sent", what, runId);
        }
    }

    // The branch the trigger named, kept only when it is a branch name: the refusal may be about it.
    private static string? NamedBranch(IReadOnlyDictionary<string, string>? additionalVariables)
        => PipelineRunService.ResolveRunBranch(additionalVariables) is { } branch && PipelineBranchValidator.IsValid(branch)
            ? branch
            : null;

    /// <summary>The reason a refused run carries, as the run page shows it.</summary>
    internal static string BuildReason(string triggerSource, string reason)
        => $"The automated launch ({triggerSource}) was refused: {reason}";
}
