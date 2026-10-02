// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Releases;

/// <summary>
/// Keeps creation and use apart, which the release row alone cannot: its PipelineRunId is overwritten
/// by every run that records the release again.
///
/// The build is the creator run, the runs that produced the deliverables, and the runs those started
/// through a trigger step. What they consumed are the build inputs; any other run that consumed the
/// release is a use. Nothing is inferred beyond the recorded facts: a release whose creator was never
/// recorded shows its last recording run as such, not as its creator.
/// </summary>
public sealed class ReleaseProvenanceService(IReleaseProvenanceRepository repo) : IReleaseProvenanceService
{
    /// <summary>Cap on the packages returned; <see cref="ReleaseProvenanceDto.PackageTotalCount"/> keeps
    /// the real count so the page says how many are not shown.</summary>
    internal const int MaxPackages = 300;

    public async Task<ReleaseProvenanceDto?> GetProvenanceAsync(int releaseId, CancellationToken ct = default)
    {
        var facts = await repo.GetFactsAsync(releaseId, ct).ConfigureAwait(false);
        if (facts is null) return null;

        var creatorRunId = facts.CreatedByPipelineRunId;
        var producerRunIds = facts.Deliverables.Select(item => item.PipelineRunId).Distinct().ToList();
        var buildRoots = producerRunIds.Concat(creatorRunId is { } creator ? [creator] : []).Distinct().ToList();
        var childRunIds = await repo.GetTriggeredRunIdsAsync(facts.ProjectId, buildRoots, ct).ConfigureAwait(false);
        var buildRunIds = buildRoots.Concat(childRunIds).ToHashSet();

        var uses = await CollectUsesAsync(facts, buildRunIds, ct).ConfigureAwait(false);
        var lastRecordedRunId = creatorRunId is null ? facts.LastPipelineRunId : null;

        var buildRuns = buildRunIds.ToList();
        var deliverableIds = facts.Deliverables.Select(item => item.ArtifactId).ToHashSet();
        var inputs = await repo.GetArtifactInputsOfRunsAsync(buildRuns, ct).ConfigureAwait(false);
        var packages = await repo.GetPackagesOfRunsAsync(facts.ProjectId, buildRuns, ct).ConfigureAwait(false);

        var referencedRunIds = uses.Keys
            .Concat(producerRunIds)
            .Concat(creatorRunId is { } c ? [c] : [])
            .Concat(lastRecordedRunId is { } l ? [l] : [])
            .Distinct().ToList();
        var runs = await repo.GetRunRefsAsync(facts.ProjectId, referencedRunIds, ct).ConfigureAwait(false);

        return new ReleaseProvenanceDto
        {
            CreatedBy = RunOrNull(runs, creatorRunId),
            LastRecordedBy = RunOrNull(runs, lastRecordedRunId),
            BuildRuns = producerRunIds.Where(id => id != creatorRunId)
                .Select(id => RunOrNull(runs, id)).OfType<ReleaseRunRefDto>()
                .OrderBy(run => run.StartedAt).ToList(),
            Uses = uses
                .Select(use => RunOrNull(runs, use.Key) is { } run ? new ReleaseUseDto { Run = run, Kind = use.Value } : null)
                .OfType<ReleaseUseDto>()
                .OrderByDescending(use => use.Run.StartedAt).ToList(),
            ArtifactInputs = inputs
                .Select(input => input with { IsDeliverable = input.ArtifactId is { } id && deliverableIds.Contains(id) })
                .ToList(),
            Packages = packages.Take(MaxPackages).ToList(),
            PackageTotalCount = packages.Count
        };
    }

    /// <summary>One entry per run outside the build, keeping the most telling way it used the release.</summary>
    private async Task<Dictionary<int, ReleaseUseKind>> CollectUsesAsync(
        ReleaseProvenanceFacts facts, IReadOnlySet<int> buildRunIds, CancellationToken ct)
    {
        var uses = new Dictionary<int, ReleaseUseKind>();
        void Add(int runId, ReleaseUseKind kind)
        {
            if (buildRunIds.Contains(runId)) return;
            if (!uses.TryGetValue(runId, out var existing) || Rank(kind) > Rank(existing))
                uses[runId] = kind;
        }

        var deliverableIds = facts.Deliverables.Select(item => item.ArtifactId).Distinct().ToList();
        foreach (var use in await repo.GetArtifactUsesAsync(facts.ReleaseId, deliverableIds, ct).ConfigureAwait(false))
            Add(use.PipelineRunId, use.Kind == ArtifactInputKind.Deploy ? ReleaseUseKind.Deploy : ReleaseUseKind.Restore);
        foreach (var runId in await repo.GetRollbackRunIdsAsync(facts.ReleaseId, ct).ConfigureAwait(false))
            Add(runId, ReleaseUseKind.Rollback);
        // A later run that recorded the release again is a use only when the creator is known: when it
        // is not, that run may be the creator itself and is reported apart (LastRecordedBy).
        if (facts.CreatedByPipelineRunId is not null && facts.LastPipelineRunId is { } last)
            Add(last, ReleaseUseKind.Recorded);
        return uses;
    }

    private static int Rank(ReleaseUseKind kind) => kind switch
    {
        ReleaseUseKind.Rollback => 3,
        ReleaseUseKind.Deploy => 2,
        ReleaseUseKind.Restore => 1,
        _ => 0
    };

    private static ReleaseRunRefDto? RunOrNull(Dictionary<int, ReleaseRunRefDto> runs, int? runId) =>
        runId is { } id && runs.TryGetValue(id, out var run) ? run : null;
}
