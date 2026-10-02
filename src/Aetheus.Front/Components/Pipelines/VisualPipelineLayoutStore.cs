// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Pipelines;

/// <summary>
/// S-FEAT-VPNP: persists the manually-arranged node positions of a pipeline's Visual editor in the
/// browser's localStorage (keyed by pipeline id), so a hand-laid-out graph survives a reload. A structure
/// <b>signature</b> (the ordered stage names) is stored alongside the positions and re-checked on load:
/// any structural edit - add, remove, <b>or reorder</b> a stage - changes the signature, so stale
/// index-keyed positions are never re-applied to the wrong stages (challenge follow-up). Extracted from
/// <see cref="VisualPipelineEditor"/> to keep that file under the 600-line budget.
/// </summary>
internal sealed class VisualPipelineLayoutStore(IJSRuntime js)
{
    /// <summary>Overlay saved positions onto <paramref name="positions"/> only when the saved signature matches.</summary>
    public async Task LoadIntoAsync(int pipelineId, string signature, Dictionary<string, NodePosition> positions)
    {
        var blob = await js.InvokeAsync<LayoutBlob?>("visualPipeline.loadLayout", pipelineId).ConfigureAwait(false);
        if (blob is null || blob.Sig != signature || blob.Pos is null) return;
        foreach (var (nodeId, xy) in blob.Pos)
            if (xy.Length == 2 && positions.ContainsKey(nodeId))
                positions[nodeId] = new NodePosition(xy[0], xy[1]);
    }

    public Task SaveAsync(int pipelineId, string signature, Dictionary<string, NodePosition> positions)
    {
        var payload = new LayoutBlob
        {
            Sig = signature,
            Pos = positions.ToDictionary(kv => kv.Key, kv => new[] { kv.Value.X, kv.Value.Y })
        };
        return js.InvokeVoidAsync("visualPipeline.saveLayout", pipelineId, payload).AsTask();
    }

    internal sealed class LayoutBlob
    {
        public string Sig { get; set; } = string.Empty;
        public Dictionary<string, double[]>? Pos { get; set; }
    }
}
