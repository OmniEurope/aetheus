// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Pipelines;

internal sealed class PipelineTemplateParameterState
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public PipelineTemplateSelection? Selection { get; private set; }

    public string Select(PipelineTemplateSelection selection)
    {
        Selection = selection;
        _values.Clear();
        foreach (var parameter in selection.Parameters)
            _values[parameter.Name] = parameter.Default ?? string.Empty;
        return Render();
    }

    public string Update(string name, string? value)
    {
        _values[name] = value ?? string.Empty;
        return Render();
    }

    public string GetValue(string name) => _values.GetValueOrDefault(name, string.Empty);

    public void Clear()
    {
        Selection = null;
        _values.Clear();
    }

    private string Render() => Selection is { } selection
        ? PipelineTemplateEditorCoordinator.ApplyParameters(selection, _values)
        : string.Empty;
}
