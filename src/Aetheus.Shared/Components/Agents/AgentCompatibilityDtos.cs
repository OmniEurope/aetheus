// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Shared.Components.Agents;

public sealed record AgentCompatibilityDto
{
    public AgentCompatibilityStatus Status { get; init; }
    public AgentCompatibilityReason Reason { get; init; }
    public string InstalledVersion { get; init; } = string.Empty;
    public string TargetVersion { get; init; } = string.Empty;
    public int? AgentProtocolVersion { get; init; }
    public int MinimumSupportedProtocol { get; init; }
    public int MaximumSupportedProtocol { get; init; }
    public List<string> PresentCapabilities { get; init; } = [];
    public List<string> MissingCapabilities { get; init; } = [];
}

public sealed record AgentCompatibilitySummaryDto
{
    public int UpToDate { get; init; }
    public int UpdateRecommended { get; init; }
    public int UpdateRequired { get; init; }
    public int Unknown { get; init; }
}
