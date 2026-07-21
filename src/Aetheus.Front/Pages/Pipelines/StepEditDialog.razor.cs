// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Pipelines;

public partial class StepEditDialog
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;

    [Parameter] public PipelineStepDefinition? Step { get; set; }

    private string _name = string.Empty;
    private string _shell = string.Empty;
    private int _timeout = 300;

    protected override void OnParametersSet()
    {
        if (Step is not null)
        {
            _name = Step.Name;
            _shell = Step.Shell;
            _timeout = Step.TimeoutSeconds;
        }
    }

    private void OnSave()
    {
        var result = new PipelineStepDefinition
        {
            Name = _name,
            Shell = _shell,
            TimeoutSeconds = _timeout
        };
        Dialog.Close(result);
    }

    private void OnCancel()
    {
        Dialog.Close(null);
    }
}
