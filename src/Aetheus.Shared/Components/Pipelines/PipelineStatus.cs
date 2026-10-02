// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.Pipelines;

public enum PipelineStatus
{
    Pending = 0,
    Running = 1,
    WaitingForApproval = 2,
    Success = 3,
    Failed = 4,
    Cancelled = 5,

    /// <summary>
    /// PLAN-003 D13: the run finished, nothing that could block it failed, but at least one step
    /// marked `continue_on_error` did. It is neither a success nor a failure, and it deliberately does
    /// NOT trigger an `on_success` chain.
    /// </summary>
    Partial = 6,

    /// <summary>
    /// PLAN-004 R-14: the run failed - a stage broke, or a confirmation window expired - and its rollback
    /// stage then put the previous version back, all of its steps green. The deployment did not
    /// happen and nothing is left broken, which Failed did not say. Not a success: it triggers no
    /// `on_success` chain, and a retry is refused like on any other finished deployment attempt.
    /// </summary>
    RolledBack = 7
}
