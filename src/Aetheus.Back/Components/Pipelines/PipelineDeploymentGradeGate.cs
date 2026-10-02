// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// R-368: the assurance-grade threshold of a deployment, checked by the control plane before the run
/// dispatches anything.
///
/// The threshold used to be read only by a script in the second stage of aetheus-deploy-prod, whose
/// first stage is already bound to <c>environment: prod</c>: a release graded F had its production
/// approval requested, and its System prepare and artifact restore run on the production agent,
/// before anything compared its grade with the declared E. The agent-side check stays (it verifies the
/// sealed contract itself); this one decides whether the attempt may start at all.
///
/// A run declares the threshold with <see cref="MinimumGradeVariable"/> and names the release with
/// <see cref="ReleaseVariable"/>, the two variables aetheus-deploy-prod already carries, so no
/// definition has to change and an older control plane reading the same YAML is unaffected. Every
/// doubt refuses: an unreadable threshold, no release, an unknown release, a release with no sealed grade.
/// </summary>
internal sealed class PipelineDeploymentGradeGate(IPipelineRepository repo)
{
    internal const string MinimumGradeVariable = "AETHEUS_DEPLOY_MINIMUM_GRADE";
    internal const string ReleaseVariable = "AETHEUS_CANDIDATE_VERSION";

    /// <summary>Null when the run may start, otherwise the refusal naming the grade and the threshold.</summary>
    public async Task<string?> EvaluateAsync(
        int runId, IReadOnlyDictionary<string, string> variables, CancellationToken ct)
    {
        if (!variables.TryGetValue(MinimumGradeVariable, out var rawThreshold) || string.IsNullOrWhiteSpace(rawThreshold))
            return null;
        var threshold = rawThreshold.Trim();
        // Exactly one capital letter: Enum.TryParse would also accept "4" or "e" and read a typo as a grade.
        if (threshold.Length != 1 || threshold[0] is < 'A' or > 'F')
            return $"Deployment refused before it started: {MinimumGradeVariable} '{threshold}' is not an assurance grade (A to F).";
        var minimum = Enum.Parse<AnalysisGrade>(threshold);

        var selector = variables.TryGetValue(ReleaseVariable, out var rawRelease) ? rawRelease.Trim() : string.Empty;
        if (selector.Length == 0 || selector.Contains("$(", StringComparison.Ordinal))
            return $"Deployment refused before it started: the deployment threshold {threshold} is declared but "
                + $"{ReleaseVariable} names no release ('{selector}'), so no grade can be checked.";

        var run = await repo.GetPipelineRunWithPipelineAsync(runId, ct).ConfigureAwait(false);
        var projectId = run?.Pipeline is null
            ? null
            : await repo.GetPipelineProjectIdAsync(run.Pipeline, ct).ConfigureAwait(false);
        if (projectId is null)
            return $"Deployment refused before it started: release '{selector}' cannot be checked against the "
                + $"deployment threshold {threshold} because the run is not attached to a project.";

        var release = await repo.FindDeploymentGateReleaseAsync(projectId.Value, selector, ct).ConfigureAwait(false);
        if (release is null)
            return $"Deployment refused before it started: release '{selector}' does not exist in this project, "
                + $"so the deployment threshold {threshold} cannot be checked.";
        if (release.AssuranceGrade is not { } grade)
            return $"Deployment refused before it started: release '{release.Version}' has no sealed assurance grade, "
                + $"and the deployment threshold {threshold} requires one.";
        return grade > minimum
            ? $"Deployment refused before it started: release '{release.Version}' has assurance grade {grade}, "
                + $"below the deployment threshold {threshold}."
            : null;
    }
}
