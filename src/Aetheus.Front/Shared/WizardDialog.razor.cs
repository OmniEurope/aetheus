// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Components.Web;

namespace Aetheus.Front.Shared;

public partial class WizardDialog : IAsyncDisposable
{
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter, EditorRequired] public List<WizardStep> Steps { get; set; } = [];
    [Parameter] public bool CanAdvance { get; set; } = true;
    /// <summary>
    /// Optional veto invoked with the current step index before advancing on Next. Return false to
    /// stay on the step (the validator is responsible for surfacing why, e.g. an error dialog).
    /// Keeps Next clickable so the user gets feedback instead of a silently-disabled button.
    /// </summary>
    [Parameter] public Func<int, Task<bool>>? ValidateBeforeNext { get; set; }
    [Parameter] public string? FinishText { get; set; }
    [Parameter] public double? ProgressOverride { get; set; }
    [Parameter] public EventCallback<int> StepChanged { get; set; }
    [Parameter] public EventCallback OnFinishClicked { get; set; }
    [Parameter] public EventCallback OnCancelClicked { get; set; }

    private ElementReference _wizardRef;
    private int _currentStep;
    private int _highestStep;
    private bool _suppressChange;
    private double _progress;

    public int CurrentStep => _currentStep;

    private void UpdateProgress()
    {
        _progress = ProgressOverride.HasValue
            ? Math.Clamp(ProgressOverride.Value, 0, 100)
            : Steps.Count > 1
                ? Math.Round((double)_currentStep / (Steps.Count - 1) * 100, 0)
                : 100;
    }

    private bool IsStepAccessible(int index) => index <= _highestStep;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
            await _wizardRef.FocusAsync();
    }

    protected override void OnParametersSet() => UpdateProgress();

    public async Task NextStep()
    {
        if (_currentStep < Steps.Count - 1)
        {
            if (ValidateBeforeNext is not null && !await ValidateBeforeNext(_currentStep))
                return;
            _suppressChange = true;
            _currentStep++;
            _highestStep = Math.Max(_highestStep, _currentStep);
            UpdateProgress();
            await StepChanged.InvokeAsync(_currentStep);
            _suppressChange = false;
        }
    }

    private async Task PreviousStep()
    {
        if (_currentStep > 0)
        {
            _suppressChange = true;
            _currentStep--;
            UpdateProgress();
            await StepChanged.InvokeAsync(_currentStep);
            _suppressChange = false;
        }
    }

    private async Task OnStepChanged(int index)
    {
        if (_suppressChange)
        {
            _currentStep = index;
            return;
        }
        _currentStep = index;
        _highestStep = Math.Max(_highestStep, _currentStep);
        UpdateProgress();
        await StepChanged.InvokeAsync(_currentStep);
    }

    private async Task OnCancel()
    {
        if (OnCancelClicked.HasDelegate)
            await OnCancelClicked.InvokeAsync();
        else
            Dialog.Close();
    }

    private async Task OnFinish()
    {
        if (OnFinishClicked.HasDelegate)
            await OnFinishClicked.InvokeAsync();
        else
            Dialog.Close(true);
    }

    private async Task OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
            await OnCancel();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class WizardStep
{
    public required string Title { get; init; }
    public required RenderFragment Content { get; init; }
    public string? Icon { get; init; }
}
