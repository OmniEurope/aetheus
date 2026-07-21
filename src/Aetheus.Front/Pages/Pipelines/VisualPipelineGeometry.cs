// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Pages.Pipelines;

/// <summary>
/// S-TECH-VPNH: single source for the Visual editor's node geometry and edge path building. The node-height
/// formula is mirrored (by necessity) in <c>visual-pipeline.js</c> (dagre layout) and <c>app.css</c>
/// (<c>.vp-node</c>); keeping the C# copy here - and preferring a real measured <c>offsetHeight</c> when the
/// editor has one - stops the three from drifting and mis-anchoring edges.
/// </summary>
internal static class VisualPipelineGeometry
{
    public const double NodeWidth = 220;
    public const double NodeBaseHeight = 100;
    public const double StepRowHeight = 28;

    /// <summary>Fallback node height when no measured value is available (matches JS + CSS).</summary>
    public static double NodeHeight(int stepCount) => NodeBaseHeight + Math.Max(0, stepCount - 1) * StepRowHeight;

    /// <summary>
    /// S-DES-VPBZ: a cubic Bézier with horizontal tangents, so an LR edge between vertically-misaligned
    /// nodes curves smoothly instead of drawing a straight diagonal that can cut through an intermediate
    /// node. Coordinates are emitted with <c>InvariantCulture</c> (fr-FR's decimal comma would corrupt the
    /// path - see SvgCultureInvariantAuditTests).
    /// </summary>
    public static string BuildEdgePath(double x1, double y1, double x2, double y2)
    {
        var dx = Math.Max(40, Math.Abs(x2 - x1) * 0.5);
        return System.FormattableString.Invariant(
            $"M {x1:F1} {y1:F1} C {x1 + dx:F1} {y1:F1} {x2 - dx:F1} {y2:F1} {x2:F1} {y2:F1}");
    }
}
