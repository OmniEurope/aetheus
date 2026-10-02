// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// What the launch preflight reads about a configured runner: the facts its agent last reported,
/// raw as persisted. Nothing here is probed live; it is the control plane's own view.
/// </summary>
public sealed record PipelineRunnerFacts(
    int Id,
    string Name,
    ServerStatus Status,
    int? AgentProtocolVersion,
    IReadOnlyList<string> AgentCapabilities,
    string? ScannerCapabilitiesJson);
