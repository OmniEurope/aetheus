// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// S-TECH-K7QX: census of base &lt;-&gt; child name collisions across a template <c>extends</c> merge.
/// Today the merge <b>appends</b> the child's stages to the base's (<see cref="PipelineTemplateService"/>),
/// so a child stage that reuses a base stage's name yields two same-named stages - a deliberate append.
/// PLAN-003 §4.4/§6 plans <c>replace-by-name</c> semantics, under which that same authoring would
/// silently flip from "append a second stage" to "override the base stage". This detector enumerates the
/// homonyms (stage, and job/step within same-named stages) so the risk is surfaced <em>before</em> the
/// semantics change - both as a build-time census (see the guard test) and as a runtime warning at merge.
/// </summary>
internal static class PipelineExtendsCollisionDetector
{
    /// <summary>
    /// Returns a human-readable path for every base/child name collision the append merge would produce
    /// (empty when there is none). Names are matched case-insensitively, mirroring the variable merge.
    /// </summary>
    public static IReadOnlyList<string> FindCollisions(PipelineYamlDefinition baseDef, PipelineYamlDefinition childDef)
    {
        ArgumentNullException.ThrowIfNull(baseDef);
        ArgumentNullException.ThrowIfNull(childDef);

        var collisions = new List<string>();
        foreach (var childStage in childDef.Stages)
        {
            var baseStage = baseDef.Stages.FirstOrDefault(
                s => NameEquals(s.Name, childStage.Name) && !string.IsNullOrEmpty(childStage.Name));
            if (baseStage is null) continue;

            collisions.Add($"stage '{childStage.Name}'");

            foreach (var job in childStage.Jobs)
                if (!string.IsNullOrEmpty(job.Name) && baseStage.Jobs.Any(b => NameEquals(b.Name, job.Name)))
                    collisions.Add($"stage '{childStage.Name}' > job '{job.Name}'");

            foreach (var step in childStage.Steps)
                if (!string.IsNullOrEmpty(step.Name) && baseStage.Steps.Any(b => NameEquals(b.Name, step.Name)))
                    collisions.Add($"stage '{childStage.Name}' > step '{step.Name}'");
        }

        return collisions;
    }

    private static bool NameEquals(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
