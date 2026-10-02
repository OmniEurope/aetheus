// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.Agents;

public enum AgentUpdateRequestStatus
{
    WaitingForIdle = 0,
    Queued = 1,
    Downloading = 2,
    Staging = 3,
    Handoff = 4,
    Confirmed = 5,
    Failed = 6
}
