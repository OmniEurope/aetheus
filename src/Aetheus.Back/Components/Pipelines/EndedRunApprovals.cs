// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// PLAN-005 lot 3 / D34. An approval can only be waited on while its run is alive. Run 2323 failed,
/// asked for a Rollback approval, was cancelled twelve hours later, and that approval stayed Pending
/// with nothing left to approve: the run page kept a decision banner for a run that was over. When a
/// run ends, its pending approvals are closed as Rejected with this reason; the run keeps its own
/// status (Failed, Cancelled...). No new enum value: Rejected already means "this will not proceed".
/// </summary>
public static class EndedRunApprovals
{
    public const string Reason = "Closed automatically: the run ended before this approval was decided.";
}
