// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Enums;

public enum ReleaseStatus
{
    Detected = 0,
    Building = 1,
    Published = 2,
    Failed = 3,
    RolledBack = 4,
    Promoted = 5,

    /// <summary>Cross-agent deployment: the release artifact was applied on a deployment-capable agent
    /// (binary flip + restart, or container compose up) and the app passed the post-deploy health gate.
    /// Set by the deploy-success closure: a successful <c>type: deploy</c> task flips any linked
    /// release here (via <c>IArtifactService.MarkDeployedAsync</c> from <c>TaskService.CompleteTaskAsync</c>)
    /// and applies the Deployed retention window. No migration - the column is the same <c>int</c> enum.</summary>
    Deployed = 6
}
