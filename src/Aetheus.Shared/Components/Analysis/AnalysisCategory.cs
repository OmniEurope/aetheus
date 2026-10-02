// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.Analysis;

public enum AnalysisCategory
{
    Sast = 0,
    Secrets = 1,
    Dependencies = 2,
    Container = 3,
    InfrastructureAsCode = 4,
    Sbom = 5,
    Dast = 6,
    CodeQuality = 7,
    Coverage = 8,
    Duplication = 9,
    Architecture = 10,
    /// <summary>axe-core violations of the E2E suite (PLAN-003 2.4), published by a lint step.</summary>
    Accessibility = 11
}
