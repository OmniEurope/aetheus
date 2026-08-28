// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Pipelines;

public partial class PipelineTemplateExtract : PipelineTemplateExtractionBase
{
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;

    private bool _loading = true;
    private bool _loadFailed;
    private int _loadedPipelineId;

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedPipelineId == PipelineId) return;
        _loadedPipelineId = PipelineId;
        _loading = true;
        try
        {
            var pipeline = await Api.Pipelines.GetPipelineAsync(PipelineId);
            if (pipeline is null) { _loadFailed = true; return; }
            _model = new PipelineTemplateExtractionModel
            {
                TemplateName = pipeline.Name,
                TemplateYamlContent = pipeline.YamlDefinition,
                RewrittenPipelineYaml = PipelineTemplateExtractionModel.BuildPinnedYaml(pipeline.Name, pipeline.Name)
            };
            PipelineActionBreadcrumbs.Set(Breadcrumb, L, PipelineId, pipeline, L["ExtractAsTemplate"]);
        }
        catch (HttpRequestException) { _loadFailed = true; }
        finally { _loading = false; }
    }

    private async Task SubmitAsync()
    {
        try
        {
            var result = await ExtractAsync();
            if (result is null)
            {
                Toast.Error("Error", "SaveFailed");
                return;
            }
            Toast.Success("Created", result.Name);
            Nav.NavigateTo($"/templates/{result.Id}");
        }
        catch (HttpRequestException) { Toast.Error("Error", "SaveFailed"); }
    }

    private void Cancel() => Nav.NavigateTo($"/pipelines/{PipelineId}");

}
