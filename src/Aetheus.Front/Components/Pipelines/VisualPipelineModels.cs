// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Components.Pipelines;

// View models and JS-interop DTOs for the visual pipeline editor (VisualPipelineEditor).
// Extracted from the component file to keep it under the 600-line budget (FileSizeAuditTests).

internal sealed record NodePosition(double X, double Y);

internal sealed record EdgeData(string From, string To, string PathData);

internal sealed class LayoutResult
{
    public List<LayoutNode> Nodes { get; set; } = [];
    public List<LayoutEdge> Edges { get; set; } = [];
}

internal sealed class LayoutNode
{
    public string Id { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

internal sealed class LayoutEdge
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public List<PointData> Points { get; set; } = [];
}

internal sealed class PointData
{
    public double X { get; set; }
    public double Y { get; set; }
}
