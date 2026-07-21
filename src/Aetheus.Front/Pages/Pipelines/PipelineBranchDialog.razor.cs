// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Pipelines;

/// <summary>Chooses a branch for one pipeline run without changing the pipeline definition.</summary>
public partial class PipelineBranchDialog
{
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter, EditorRequired] public List<GitLightBranchDto> Branches { get; set; } = [];
    [Parameter] public string? SourceBranch { get; set; }

    private string? _branch;

    protected override void OnInitialized() =>
        _branch = Branches.FirstOrDefault(branch => branch.IsDefault)?.Name
            ?? Branches.FirstOrDefault(branch => string.Equals(branch.Name, SourceBranch, StringComparison.Ordinal))?.Name
            ?? Branches.FirstOrDefault()?.Name;

    private void Confirm() => Dialog.Close(_branch);
    private void Cancel() => Dialog.Close(null);
}
