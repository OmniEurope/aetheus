// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.ExternalRepos;
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// S-TECH-RPVI: single source of truth for the project-level system variables a run injects
/// (<c>REPOSITORY_URL</c> / <c>BUILD_REPOSITORY_URI</c> / branch / project name). Both the trigger-time
/// snapshot (<see cref="PipelineRunService"/>) and the per-run re-resolution
/// (<see cref="PipelineVariableResolver"/>) call this, so they can never diverge again. The divergence
/// is exactly what froze the attach-time <c>localhost:5300</c> authority on one path but rehomed it on the
/// other, breaking the first-stage clone on a remote agent.
/// </summary>
internal static class PipelineProjectVariables
{
    /// <summary>
    /// Adds the project system variables into <paramref name="vars"/> with <c>TryAdd</c> semantics
    /// (caller-supplied overrides win). The mirror URL is re-homed onto the executing backend's clone
    /// base via <see cref="MirrorCloneUrl.Rehome"/> (a no-op for any non-mirror URL).
    /// </summary>
    public static void Inject(
        IDictionary<string, string> vars,
        Project? project,
        IConfiguration configuration,
        string? sourceBranch = null)
    {
        if (project is null) return;

        var repositoryUrl = MirrorCloneUrl.Rehome(configuration, project.RepositoryUrl) ?? string.Empty;
        var branch = string.IsNullOrWhiteSpace(sourceBranch)
            ? project.DefaultBranch ?? "main"
            : sourceBranch;
        vars.TryAdd("BUILD_REPOSITORY_URI", repositoryUrl);
        vars.TryAdd("BUILD_SOURCEBRANCH", branch);
        vars.TryAdd("BUILD_PROJECTNAME", project.Name);
        vars.TryAdd("REPOSITORY_URL", repositoryUrl);
        vars.TryAdd("DEFAULT_BRANCH", branch);
    }
}
