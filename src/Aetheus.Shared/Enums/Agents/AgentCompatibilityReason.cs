// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Enums;

public enum AgentCompatibilityReason
{
    NoHeartbeat = 0,
    Current = 1,
    OlderSoftware = 2,
    OptionalCapabilityMissing = 3,
    UnsupportedProtocol = 4,
    InvalidSoftwareVersion = 5,
    RequiredCapabilityMissing = 6
}
