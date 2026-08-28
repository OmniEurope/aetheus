// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Pages.Pipelines;

// P: queue-time run-parameters dialog. Returns the effective name→value map (string-encoded) via
// DialogService.Close, or null on cancel. The inputs themselves live in RunParameterFields, shared
// with the unified launch dialog.
public partial class RunParametersDialog
{
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter, EditorRequired] public List<PipelineRunParameterDto> Parameters { get; set; } = [];

    // S-UX-K7P2: optional seed values (e.g. the source run's parameters on re-run) that override the
    // declared defaults so the dialog opens prefilled with what the previous run used.
    [Parameter] public IReadOnlyDictionary<string, string>? Prefill { get; set; }
    [Parameter] public string? SubmitText { get; set; }
    [Parameter] public string SubmitIcon { get; set; } = "play_arrow";

    /// <summary>Recent project releases, passed through to the fields as a picking aid.</summary>
    [Parameter] public List<ReleaseDto> AvailableReleases { get; set; } = [];

    private RunParameterFields? _fields;
    private string? _error;

    private void OnSubmit()
    {
        if (_fields is null || !_fields.TryCollect(out var values, out _error)) return;
        Dialog.Close(values);
    }

    private void OnCancel() => Dialog.Close(null);
}
