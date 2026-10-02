// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Pipelines;

public partial class PipelineTemplatePromote : PipelineTemplatePromotionBase
{
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;

    private int _loadedPipelineId;

    protected override Task OnParametersSetAsync()
    {
        if (_loadedPipelineId == PipelineId) return Task.CompletedTask;
        _loadedPipelineId = PipelineId;
        return LoadPreviewAsync();
    }

    protected override async Task OnPreviewLoadedAsync(PipelinePromotePreviewDto preview)
    {
        var pipeline = await Api.Pipelines.GetPipelineAsync(PipelineId).ConfigureAwait(false);
        PipelineActionBreadcrumbs.Set(Breadcrumb, L, PipelineId, pipeline, L["PromoteToTemplate"]);
    }

    private async Task SubmitAsync()
    {
        try
        {
            var result = await PromoteAsync();
            if (result is null)
            {
                Toast.Error("Error", "SaveFailed");
                return;
            }
            Toast.Success("Published", result.Name);
            Nav.NavigateTo($"/templates/{result.Id}/versions");
        }
        catch (HttpRequestException) { Toast.Error("Error", "SaveFailed"); }
    }

    private void Cancel() => Nav.NavigateTo($"/pipelines/{PipelineId}");

}
