// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;

namespace Aetheus.Front.Pages.Pipelines;

/// <summary>
/// Builds the flattened, ordered timeline driving the RadzenTimeline on the run's Progress view.
/// Group headers, stages, sub-steps and release milestones are interleaved in execution order.
/// Extracted from the former <c>PipelineRun.Timeline.cs</c> partial into a real collaborator; the
/// markup imports the nested node types via <c>@using static</c>.
/// </summary>
internal static class PipelineRunTimelineBuilder
{
    public enum TimelineNodeKind { GroupHeader, Stage, SubStep, Release }

    public sealed class TimelineNode
    {
        public TimelineNodeKind Kind { get; init; }
        public StageViewModel? Stage { get; init; }
        public PipelineStepRunDto? Step { get; init; }
        public ReleaseDto? Release { get; init; }
        public string? GroupName { get; init; }
        public bool InGroup { get; init; }
    }

    // Releases produced by a step (RELEASE_ID output variable) are inserted right after that step;
    // releases without a producing step are appended.
    public static List<TimelineNode> Build(PipelineRunDto? run, List<StageViewModel> stages, List<ReleaseDto> releases)
    {
        var nodes = new List<TimelineNode>();
        if (run is null) return nodes;

        var emittedReleaseIds = new HashSet<int>();
        string? currentGroup = null;

        void EmitReleasesFor(PipelineStepRunDto step)
        {
            if (!step.OutputVariables.TryGetValue("RELEASE_ID", out var relIdRaw)) return;
            if (!int.TryParse(relIdRaw, out var relId)) return;
            var release = releases.FirstOrDefault(r => r.Id == relId);
            if (release is null || !emittedReleaseIds.Add(release.Id)) return;
            nodes.Add(new TimelineNode { Kind = TimelineNodeKind.Release, Release = release, InGroup = step.GroupName is not null });
        }

        foreach (var stage in stages)
        {
            if (stage.GroupName != currentGroup)
            {
                currentGroup = stage.GroupName;
                if (currentGroup is not null)
                    nodes.Add(new TimelineNode { Kind = TimelineNodeKind.GroupHeader, GroupName = currentGroup });
            }

            var inGroup = stage.GroupName is not null;
            if (stage.Steps.Count == 1)
            {
                var step = stage.Steps[0];
                nodes.Add(new TimelineNode { Kind = TimelineNodeKind.Stage, Stage = stage, Step = step, InGroup = inGroup });
                EmitReleasesFor(step);
            }
            else
            {
                nodes.Add(new TimelineNode { Kind = TimelineNodeKind.Stage, Stage = stage, InGroup = inGroup });
                foreach (var step in stage.Steps)
                {
                    nodes.Add(new TimelineNode { Kind = TimelineNodeKind.SubStep, Stage = stage, Step = step, InGroup = inGroup });
                    EmitReleasesFor(step);
                }
            }
        }

        // Releases not tied to any step (e.g. detected post-run) close the timeline as standalone milestones.
        foreach (var release in releases.Where(r => !emittedReleaseIds.Contains(r.Id)))
            nodes.Add(new TimelineNode { Kind = TimelineNodeKind.Release, Release = release });

        return nodes;
    }
}
