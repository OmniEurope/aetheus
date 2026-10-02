// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Pipelines;

public partial class ExtractPipelineTemplateDialog : PipelineTemplateExtractionBase
{
    [Inject] private OmniDialogService Dialog { get; set; } = default!;
    [Parameter] public string SuggestedName { get; set; } = string.Empty;
    [Parameter] public string SourceYaml { get; set; } = string.Empty;
    private (int PipelineId, string SuggestedName, string SourceYaml)? _initializedKey;

    protected override void OnParametersSet()
    {
        var key = (PipelineId, SuggestedName, SourceYaml);
        if (_initializedKey == key) return;
        _initializedKey = key;
        _model = new PipelineTemplateExtractionModel
        {
            TemplateName = SuggestedName,
            TemplateYamlContent = SourceYaml,
            RewrittenPipelineYaml = PipelineTemplateExtractionModel.BuildPinnedYaml(SuggestedName, SuggestedName)
        };
    }

    private async Task SubmitAsync()
    {
        var result = await ExtractAsync();
        Dialog.Close(result is not null);
    }

    private void Cancel() => Dialog.Close(false);

}
