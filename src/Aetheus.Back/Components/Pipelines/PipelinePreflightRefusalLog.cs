// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// Recette R2-040: how a preflight refusal is written to the log. One Warning counts the refusal, then
/// one Warning per unmet requirement, each in its own short attribute. The whole list used to travel in
/// a single attribute, which exceeded the OTLP ingest bound on a record's attributes and was dropped
/// entirely, leaving "6 unmet requirement(s)" with no way to read which. These are the only warnings a
/// refusal writes: the launcher and the scheduler, which used to repeat it as two more warnings
/// (recette R-521), note it at Information.
/// </summary>
internal static class PipelinePreflightRefusalLog
{
    /// <summary>A problem longer than this is cut in the log (never in the refusal itself), so one
    /// entry stays under the ingest bound.</summary>
    internal const int MaxLoggedProblemLength = 500;

    public static void Write(ILogger logger, string pipelineName, IReadOnlyList<string> problems)
    {
        logger.LogWarning(
            "Preflight refused a launch of pipeline {PipelineName} with {ProblemCount} unmet requirement(s)",
            pipelineName, problems.Count);
        for (var index = 0; index < problems.Count; index++)
        {
            var problem = problems[index];
            if (problem.Length > MaxLoggedProblemLength)
                problem = string.Concat(problem.AsSpan(0, MaxLoggedProblemLength), " [...]");
            logger.LogWarning(
                "Preflight unmet requirement {ProblemNumber}/{ProblemCount} of pipeline {PipelineName}: {Problem}",
                index + 1, problems.Count, pipelineName, problem);
        }
    }
}
