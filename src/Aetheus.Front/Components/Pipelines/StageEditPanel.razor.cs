// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Pipelines;

public partial class StageEditPanel
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter, EditorRequired] public PipelineStageDefinition Stage { get; set; } = default!;
    [Parameter] public List<string> AvailableServers { get; set; } = [];
    [Parameter] public List<string> OtherStageNames { get; set; } = [];
    [Parameter] public EventCallback<PipelineStageDefinition> StageChanged { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }
    [Parameter] public EventCallback OnDeleteStage { get; set; }
    [Parameter] public EventCallback<int> EditStep { get; set; }
    [Parameter] public EventCallback AddStep { get; set; }

    private static readonly string[] OsOptions = ["linux", "windows"];

    private Dictionary<string, string> _variables = [];
    private string _newVarKey = string.Empty;
    private string _newVarValue = string.Empty;

    protected override void OnParametersSet()
    {
        _variables = new Dictionary<string, string>(Stage.Variables);
    }

    private async Task EmitChange(PipelineStageDefinition updated)
    {
        await StageChanged.InvokeAsync(updated);
    }

    private async Task OnNameChanged(string value)
    {
        await EmitChange(Stage with { Name = value });
    }

    private async Task OnAgentChanged(string? value)
    {
        await EmitChange(Stage with { Agent = value ?? string.Empty });
    }

    private async Task OnOsChanged(string? value)
    {
        await EmitChange(Stage with { Os = string.IsNullOrEmpty(value) ? null : value });
    }

    private async Task OnDependsOnChanged(IEnumerable<string>? values)
    {
        await EmitChange(Stage with { DependsOn = values?.ToList() ?? [] });
    }

    private async Task OnVariableValueChanged(string key, string value)
    {
        _variables[key] = value;
        await EmitChange(Stage with { Variables = new Dictionary<string, string>(_variables) });
    }

    private async Task OnRemoveVariable(string key)
    {
        _variables.Remove(key);
        await EmitChange(Stage with { Variables = new Dictionary<string, string>(_variables) });
    }

    private async Task OnAddVariable()
    {
        if (string.IsNullOrWhiteSpace(_newVarKey)) return;
        _variables[_newVarKey] = _newVarValue;
        await EmitChange(Stage with { Variables = new Dictionary<string, string>(_variables) });
        _newVarKey = string.Empty;
        _newVarValue = string.Empty;
    }

    private async Task OnMoveStep(int index, int direction)
    {
        var steps = Stage.Steps.ToList();
        var target = index + direction;
        if (target < 0 || target >= steps.Count) return;
        (steps[index], steps[target]) = (steps[target], steps[index]);
        await EmitChange(Stage with { Steps = steps });
    }

    private async Task OnRemoveStep(int index)
    {
        var steps = Stage.Steps.ToList();
        steps.RemoveAt(index);
        await EmitChange(Stage with { Steps = steps });
    }

    private async Task OnCloseClicked() => await OnClose.InvokeAsync();
    private async Task OnDeleteClicked() => await OnDeleteStage.InvokeAsync();
}
