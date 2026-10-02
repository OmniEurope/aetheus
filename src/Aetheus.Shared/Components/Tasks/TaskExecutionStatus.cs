// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.Tasks;

public enum TaskExecutionStatus
{
    Pending = 0,
    Assigned = 1,
    Running = 2,
    Success = 3,
    Failed = 4,
    Timeout = 5,
    Cancelled = 6
}
