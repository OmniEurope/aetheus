// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.Agents;

public enum AgentUpdateQueueOutcome
{
    Queued = 0,
    WaitingForIdle = 1,
    ExistingRequest = 2,
    AlreadyUpToDate = 3,
    Offline = 4
}
