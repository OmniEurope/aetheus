// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Pipelines;

/// <summary>Reviews a pipeline definition before its Git-authoritative update is committed.</summary>
public partial class PipelineSaveDialog
{
    [Inject] private OmniDialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter, EditorRequired] public string SavedYaml { get; set; } = string.Empty;
    [Parameter, EditorRequired] public string DraftYaml { get; set; } = string.Empty;
    [Parameter] public List<string> Branches { get; set; } = [];
    [Parameter] public string? SourceBranch { get; set; }

    private string? _branch;

    protected override void OnInitialized() => _branch = SourceBranch ?? Branches.FirstOrDefault();

    private void Confirm() => Dialog.Close(new PipelineSaveDecision(_branch));
    private void Cancel() => Dialog.Close(null);
}

public sealed record PipelineSaveDecision(string? SourceBranch);
