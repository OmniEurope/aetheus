// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.Agents;

/// <summary>
/// Lifecycle phases of an agent self-update. Emitted by the agent as it walks through
/// <c>AgentSelfUpdateOperationExecutor</c>, forwarded by the backend on the server hub as
/// <c>AgentUpdateProgress</c>, and rendered on the server detail page as a determinate
/// progress bar (item #3 of the plan).
///
/// Ordering matters: phases are monotonically increasing - the UI compares to the previous
/// phase and ignores any out-of-order event (network reordering on the SignalR channel).
/// </summary>
public enum AgentUpdatePhase
{
    /// <summary>Pre-flight: task is queued, agent has not yet picked it up.</summary>
    Queued = 0,
    /// <summary>Agent has pulled the task and started the self-update operation.</summary>
    PickedUp = 1,
    /// <summary>Agent is downloading the new build archive from the backend.</summary>
    Downloading = 2,
    /// <summary>Download finished and integrity check passed.</summary>
    Downloaded = 3,
    /// <summary>Extracting the archive into the staging directory.</summary>
    Extracting = 4,
    /// <summary>Detached updater launched; the agent process is about to exit.</summary>
    LaunchingUpdater = 5,
    /// <summary>Agent process has gone offline (inferred from <c>ServerOffline</c>).</summary>
    AgentOffline = 6,
    /// <summary>Agent restarted on the new version (next heartbeat post-update).</summary>
    Done = 7,
    /// <summary>Update failed at some phase; the previous phase is preserved for context.</summary>
    Failed = 99
}
