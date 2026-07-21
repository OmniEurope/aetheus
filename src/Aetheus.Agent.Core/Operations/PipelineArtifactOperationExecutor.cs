// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Executors;
using Aetheus.Agent.Core.Services;
using Aetheus.Shared.Enums;

namespace Aetheus.Agent.Core.Operations;

public sealed class PipelineArtifactOperationExecutor(
    IServerApiClient apiClient,
    ILogger<PipelineArtifactOperationExecutor> logger) : IOperationExecutor
{
    public bool CanHandle(OperationKind kind) => kind is
        OperationKind.PipelineCollectArtifacts or
        OperationKind.PipelineCreateRelease or
        OperationKind.PipelineSubstituteVariables or
        OperationKind.PipelinePublishCoverage or
        OperationKind.PipelinePublishLint or
        OperationKind.PipelinePublishComplexity or
        OperationKind.PipelineRestoreArtifacts;

    public Task<ExecutorResult> ExecuteAsync(
        OperationKind kind, string target, int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput, CancellationToken cancellationToken) =>
        ExecuteAsync(kind, target, new Dictionary<string, string>(), timeoutSeconds, onOutput, cancellationToken);

    public async Task<ExecutorResult> ExecuteAsync(
        OperationKind kind, string target, IReadOnlyDictionary<string, string> envVars,
        int timeoutSeconds, Func<string, TaskLogLevel, Task> onOutput, CancellationToken cancellationToken)
    {
        return kind switch
        {
            OperationKind.PipelineCollectArtifacts => await PipelineArtifactCollector.CollectAsync(
                apiClient, logger, target, envVars, onOutput, cancellationToken).ConfigureAwait(false),
            OperationKind.PipelineCreateRelease => await PipelineReleasePublisher.PublishAsync(
                apiClient, logger, target, envVars, onOutput, cancellationToken).ConfigureAwait(false),
            OperationKind.PipelineSubstituteVariables => await PipelineVariableSubstituter.SubstituteAsync(
                target, envVars, onOutput, cancellationToken).ConfigureAwait(false),
            OperationKind.PipelinePublishCoverage => await PipelineCoveragePublisher.PublishAsync(
                apiClient, logger, target, envVars, onOutput, cancellationToken).ConfigureAwait(false),
            OperationKind.PipelinePublishLint => await PipelineQualityPublisher.PublishLintAsync(
                apiClient, logger, target, envVars, onOutput, cancellationToken).ConfigureAwait(false),
            OperationKind.PipelinePublishComplexity => await PipelineQualityPublisher.PublishComplexityAsync(
                apiClient, logger, envVars, onOutput, cancellationToken).ConfigureAwait(false),
            OperationKind.PipelineRestoreArtifacts => await PipelineArtifactRestorer.RestoreAsync(
                apiClient, logger, envVars, onOutput, cancellationToken).ConfigureAwait(false),
            _ => new ExecutorResult(-1, false)
        };
    }
}
