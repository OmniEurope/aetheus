// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Analysis;

public sealed record ParsedAnalysisComponent
{
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string? PackageUrl { get; init; }
    public string? ComponentType { get; init; }
    public IReadOnlyList<string> Licenses { get; init; } = [];
    public string? Hash { get; init; }
    public bool IsDirect { get; init; }
}
