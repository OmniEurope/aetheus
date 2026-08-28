// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Operations;

internal sealed record PipelinePublicationContext(
    int RunId,
    string? StageName,
    string BaseDirectory,
    DateTime StartedAt)
{
    internal static async ValueTask<PipelinePublicationContext?> CreateAsync(
        IReadOnlyDictionary<string, string> environment,
        TimeProvider timeProvider,
        Func<string, TaskLogLevel, Task> onOutput)
    {
        var startedAt = timeProvider.GetUtcNow().UtcDateTime;
        environment.TryGetValue("AETHEUS_RUN_ID", out var runIdText);
        environment.TryGetValue("AETHEUS_STAGE_NAME", out var stageName);
        environment.TryGetValue("AETHEUS_WORKING_DIR", out var workingDirectory);
        if (!int.TryParse(runIdText, out var runId))
        {
            await onOutput("Missing AETHEUS_RUN_ID", TaskLogLevel.Error).ConfigureAwait(false);
            return null;
        }
        var baseDirectory = !string.IsNullOrEmpty(workingDirectory)
            ? workingDirectory
            : Directory.GetCurrentDirectory();
        return new PipelinePublicationContext(runId, stageName, baseDirectory, startedAt);
    }
}
