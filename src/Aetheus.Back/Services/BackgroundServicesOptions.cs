// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Configuration;

namespace Aetheus.Back.Services;

/// <summary>
/// Configuration for background timeout services. Bound from "BackgroundServices" section.
/// </summary>
public sealed class BackgroundServicesOptions
{
    /// <summary>How often the server-heartbeat timeout sweep runs.</summary>
    public TimeSpan ServerCheckInterval { get; set; } = BackendRuntimeDefaults.SchedulerCheckInterval;

    /// <summary>A server is marked offline if no heartbeat is received for this long.</summary>
    public TimeSpan ServerHeartbeatTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>How often the task timeout sweep runs.</summary>
    public TimeSpan TaskCheckInterval { get; set; } = BackendRuntimeDefaults.SchedulerCheckInterval;

    /// <summary>
    /// A Running task past this duration is force-failed with status Timeout. Covers the case where an
    /// agent dies mid-run - without this, such a task is stuck forever.
    /// </summary>
    public TimeSpan TaskTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Short ceiling for an Assigned task that an agent claimed but never started. This state should
    /// last only for the claim/start round-trip; keeping it distinct from <see cref="TaskTimeout"/>
    /// detects a stalled or incompatible poller promptly without shortening legitimate long tasks.
    /// </summary>
    public TimeSpan AssignedStartTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// S-TECH-PTMO: shorter ceiling for a task still <b>Pending</b> (never claimed by any agent). A task
    /// nobody has claimed almost always means a dead/offline agent, so there is no point waiting the full
    /// <see cref="TaskTimeout"/> before surfacing it - fail it as Timeout once it has gone this long
    /// unclaimed. Kept distinct (and shorter) from the run/claim timeout.
    /// </summary>
    public TimeSpan PendingTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How often the trigger-step reconcile sweep runs (orchestration self-heal).</summary>
    public TimeSpan TriggerReconcileInterval { get; set; } = BackendRuntimeDefaults.SchedulerCheckInterval;

    /// <summary>
    /// Grace period before the reconcile sweep touches a Running <c>type: trigger</c> step. A trigger step
    /// has no ServerTask and is completed by the child run's completion event; the grace lets that event
    /// win first, so the sweep only steps in when the event was genuinely lost (backend restart, missed
    /// hook). Kept short - the child run itself always takes longer than this to finish.
    /// </summary>
    public TimeSpan TriggerStepGrace { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Grace before re-driving a Running pipeline that has pending steps but no assigned or
    /// running step/task capable of producing another scheduler callback.</summary>
    public TimeSpan SchedulerRecoveryGrace { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Hard backstop: a trigger step still Running this long after it started, whose child run is NOT yet
    /// terminal, is force-failed so an orchestration run can never hang forever (e.g. the child chain
    /// itself is wedged). The common "child finished but the event was lost" case is resolved immediately
    /// by mirroring the child's terminal status, independent of this ceiling.
    /// </summary>
    public TimeSpan TriggerStepStuckTimeout { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// Run-level backstop: a run still <b>Running</b> this long after it started, with NO in-flight task
    /// (Pending/Assigned/Running) and NO trigger step still waiting on a child, can no longer make
    /// progress - e.g. a step failed but the run never finalized, or every task completed without the run
    /// being closed. The reconcile sweep force-fails it. Kept well above any real pipeline duration (tasks
    /// already time out at 30 min) so a healthy run is never touched; runs waiting on a child are excluded
    /// and handled by <see cref="TriggerStepStuckTimeout"/> instead.
    /// </summary>
    public TimeSpan RunStuckTimeout { get; set; } = TimeSpan.FromHours(1);
}
