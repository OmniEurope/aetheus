// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Enums;

public enum PipelineStatus
{
    Pending = 0,
    Running = 1,
    WaitingForApproval = 2,
    Success = 3,
    Failed = 4,
    Cancelled = 5
}
