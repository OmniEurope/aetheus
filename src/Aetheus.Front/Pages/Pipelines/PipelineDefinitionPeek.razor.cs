// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Pipelines;

/// <summary>
/// Read-only viewer for a referenced pipeline's live definition, shown as the right split pane on the run
/// YAML tab and the pipeline edit page. Resolves the pipeline by name within the project, renders its YAML
/// with its own clickable <c>pipeline:</c> refs (navigating within this pane), and offers wrap/copy/close.
/// </summary>
public partial class PipelineDefinitionPeek
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private IJSRuntime Js { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;

    /// <summary>The pipeline to display, set by the parent (the ref the user clicked). Null hides the pane.</summary>
    [Parameter, EditorRequired] public string? PipelineName { get; set; }
    [Parameter] public int? ProjectId { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private PipelineDto? _current;
    private string? _currentName;
    private string? _lastExternalName;
    private bool _loading;
    private bool _wrap;
    private int _requestGeneration;

    protected override async Task OnParametersSetAsync()
    {
        // Only react to the parent CHANGING the requested pipeline - internal ref navigation must not be
        // overridden by a re-render carrying the same PipelineName.
        if (PipelineName == _lastExternalName) return;
        _lastExternalName = PipelineName;
        _wrap = false;
        if (PipelineName is null)
        {
            Interlocked.Increment(ref _requestGeneration);
            _current = null;
            _currentName = null;
            _loading = false;
            return;
        }
        await PeekAsync(PipelineName);
    }

    private async Task PeekAsync(string name)
    {
        var generation = Interlocked.Increment(ref _requestGeneration);
        _currentName = name;
        _current = null;
        _loading = true;
        StateHasChanged();
        try
        {
            // Resolve by name within the same project, then fetch the full definition (list DTO may omit YAML).
            var page = await Api.Pipelines.GetPipelinesAsync(pageSize: 100, search: name, projectId: ProjectId);
            if (generation != _requestGeneration) return;
            var match = page.Items.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                var full = await Api.Pipelines.GetPipelineAsync(match.Id) ?? match;
                if (generation == _requestGeneration) _current = full;
            }
        }
        catch (HttpRequestException)
        {
            if (generation == _requestGeneration) _current = null;
        }
        finally
        {
            if (generation == _requestGeneration) _loading = false;
        }
    }

    private async Task CopyAsync()
    {
        if (string.IsNullOrEmpty(_current?.YamlDefinition)) return;
        await Js.InvokeVoidAsync("Aetheus.copyToClipboard", _current.YamlDefinition);
        Toast.Success("Copied");
    }
}
