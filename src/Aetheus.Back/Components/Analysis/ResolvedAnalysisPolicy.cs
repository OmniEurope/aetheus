// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Analysis;

internal sealed record ResolvedAnalysisPolicy(
    AnalysisPolicy Policy,
    AnalysisPolicyScope Scope,
    bool IsInherited,
    bool IsOverride,
    AnalysisPolicyScope? OverriddenScope)
{
    public bool IsEffective => Policy.Enabled;
}
