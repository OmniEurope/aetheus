// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Services;

/// <summary>Normalizes trusted task environment values to the topology visible from this agent.</summary>
internal static class PendingTaskEnvironmentNormalizer
{
    private static readonly string[] MirrorUrlKeys = ["REPOSITORY_URL", "BUILD_REPOSITORY_URI"];

    internal static void RehomeMirrorCloneUrls(
        Dictionary<string, string> environment,
        string agentServerUrl,
        ILogger logger)
    {
        foreach (var key in MirrorUrlKeys)
        {
            if (!environment.TryGetValue(key, out var url))
                continue;

            var rehomed = MirrorUrlRehomer.RehomeToAgentBase(url, agentServerUrl);
            if (string.Equals(rehomed, url, StringComparison.Ordinal))
                continue;

            environment[key] = rehomed;
            // Never log the value: the clone URL may carry embedded Git credentials.
            logger.LogInformation("Re-homed {Key} clone authority onto the agent's ServerUrl base", key);
        }
    }

    internal static void NormalizeContainerOperationWorkspace(
        PendingTaskDto task,
        string agentWorkDirectory)
    {
        if (task.PipelineRunId is not { } runId
            || !task.EnvironmentVariables.TryGetValue("AETHEUS_WORKSPACE_MODE", out var mode)
            || !string.Equals(mode, "container", StringComparison.OrdinalIgnoreCase))
            return;

        task.EnvironmentVariables["AETHEUS_WORKING_DIR"] =
            Path.Combine(agentWorkDirectory, "cw", runId.ToString());
    }
}
