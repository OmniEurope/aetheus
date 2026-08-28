// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Pipelines;

public partial class PipelineRunVariablesDialog
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private IJSRuntime Js { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;

    [Parameter] public Dictionary<string, string> Variables { get; set; } = [];

    private List<KeyValuePair<string, string>> VariableList => Variables.OrderBy(kv => kv.Key).ToList();

    // S-DES-10: built-in run variables carry the BUILD_ / SYSTEM_ prefix (see PipelineVariableResolver);
    // Non-system values have no structured provenance in the current DTO. They are therefore labelled
    // as resolved rather than claiming that manual values came from a library. Vault secrets are filtered
    // out server-side and never reach this dialog.
    private static bool IsSystemVariable(string key) =>
        key.StartsWith("BUILD_", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("SYSTEM_", StringComparison.OrdinalIgnoreCase);

    // S-UX-26: copy every resolved variable as KEY=VALUE lines.
    private async Task CopyAllAsync()
    {
        if (Variables.Count == 0) return;
        var text = string.Join("\n", VariableList.Select(kv => $"{kv.Key}={kv.Value}"));
        await Js.InvokeVoidAsync("Aetheus.copyToClipboard", text);
        Toast.Success("Copied");
    }
}
