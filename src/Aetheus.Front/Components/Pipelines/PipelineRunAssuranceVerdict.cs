// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Pipelines;

/// <summary>
/// The candidate's sealed verdict, read from the output variables the assurance seal publishes
/// (<c>CANDIDATE_ASSURANCE_GRADE</c>, <c>CANDIDATE_DEPLOYABLE</c>, <c>CANDIDATE_BLOCKING_TESTS</c>).
///
/// The seal already decided whether the package may be deployed, and said so in the step's log. A
/// green run whose candidate is NOT deployable is the case that reads wrong: the run succeeded, and
/// the one sentence that matters was in a log nobody opens.
/// </summary>
public static class PipelineRunAssuranceVerdict
{
    private const string GradeVariable = "CANDIDATE_ASSURANCE_GRADE";
    private const string DeployableVariable = "CANDIDATE_DEPLOYABLE";
    private const string BlockingVariable = "CANDIDATE_BLOCKING_TESTS";

    /// <param name="Grade">The sealed grade letter, when the seal published one.</param>
    /// <param name="Deployable">Null when the run carries no verdict at all (not a candidate).</param>
    /// <param name="BlockingTests">The tests that hold the package back, in the seal's own words.</param>
    public sealed record Verdict(string? Grade, bool? Deployable, IReadOnlyList<string> BlockingTests);

    /// <summary>Null when this run published no verdict: most runs are not candidates.</summary>
    public static Verdict? Read(PipelineRunDto? run, IReadOnlyDictionary<int, PipelineRunDto>? children = null)
    {
        if (run is null) return null;

        // The seal runs in the candidate itself, but a chain can also carry it in a child, so the
        // same scan covers both rather than being right only on the pipeline that happens to own it.
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in new[] { run }.Concat(children?.Values ?? []))
            foreach (var step in source.Steps)
                foreach (var (key, value) in step.OutputVariables)
                    variables.TryAdd(key, value);

        var hasGrade = variables.TryGetValue(GradeVariable, out var grade);
        var hasDeployable = variables.TryGetValue(DeployableVariable, out var deployableRaw);
        if (!hasGrade && !hasDeployable) return null;

        bool? deployable = hasDeployable && bool.TryParse(deployableRaw, out var parsed) ? parsed : null;
        var blocking = variables.TryGetValue(BlockingVariable, out var blockingRaw)
            ? blockingRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];

        return new Verdict(hasGrade ? grade : null, deployable, blocking);
    }
}
