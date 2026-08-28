// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.DTOs;

/// <summary>
/// Compact pipeline-local override for an inherited analysis policy. <c>minimum</c> and
/// <c>maximum</c> infer the comparison operator; <c>operator</c> + <c>threshold</c> cover
/// advanced cases.
/// </summary>
public sealed record PipelineAnalysisGateRuleDefinition
{
    public string Key { get; init; } = string.Empty;
    public string? Metric { get; init; }
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
    public string? Operator { get; init; }
    public double? Threshold { get; init; }
    public string? Category { get; init; }
    public string? Scanner { get; init; }
    public string? Rule { get; init; }
    public string? Severity { get; init; }
    public bool? NewFindingsOnly { get; init; }
    public string? Branch { get; init; }
    public string? Environment { get; init; }
    public string? Behavior { get; init; }
    public int? Priority { get; init; }
    public bool? Enabled { get; init; }
}
