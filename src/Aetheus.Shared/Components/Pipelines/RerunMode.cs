// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.Pipelines;

/// <summary>How a pipeline run is re-launched from an existing run (G: the "Relancer" split-button).</summary>
public enum RerunMode
{
    /// <summary>Re-run the pipeline's current definition against the branch head (a fresh normal run).</summary>
    Current = 0,

    /// <summary>Reproduce the run exactly: its captured YAML snapshot pinned to the same commit.</summary>
    SnapshotSameCommit = 1,

    /// <summary>Re-run the captured YAML snapshot but against the current branch head commit.</summary>
    SnapshotBranchHead = 2,

    /// <summary>Re-run the captured definition at the same commit and reuse only fully verified child checkpoints.</summary>
    ResumeCheckpoints = 3
}
