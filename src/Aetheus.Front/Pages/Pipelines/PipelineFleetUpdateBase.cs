// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Pipelines;

public abstract class PipelineFleetUpdateBase : ComponentBase
{
    [Inject] protected ApiClient Api { get; set; } = default!;
    [Inject] protected IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] protected NotifyHelper Toast { get; set; } = default!;

    [Parameter] public int PipelineId { get; set; }
    [Parameter] public int TargetVersion { get; set; }

    protected PipelineFleetUpdatePreviewDto? _preview;
    protected bool _loading = true;
    protected bool _loadFailed;
    protected bool _saving;
    protected bool _acknowledged;

    protected async Task LoadPreviewAsync()
    {
        _loading = true;
        _loadFailed = false;
        _preview = null;
        try
        {
            _preview = await Api.Pipelines.PreviewPipelineFleetUpdateAsync(PipelineId, TargetVersion)
                .ConfigureAwait(false);
            _loadFailed = _preview is null;
            if (_preview is not null)
                await OnPreviewLoadedAsync(_preview).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            _loadFailed = true;
        }
        finally
        {
            _loading = false;
        }
    }

    protected virtual Task OnPreviewLoadedAsync(PipelineFleetUpdatePreviewDto preview) => Task.CompletedTask;

    protected async Task<PipelineDto?> ApplyUpdateAsync()
    {
        if (_saving || _preview is null)
            return null;

        _saving = true;
        try
        {
            return await Api.Pipelines.ApplyPipelineFleetUpdateAsync(PipelineId, new PipelineFleetUpdateRequest
            {
                TargetVersion = TargetVersion,
                AcknowledgeOrphanOverrides = _acknowledged,
                ExpectedSourceYamlHash = _preview.SourceYamlHash
            }).ConfigureAwait(false);
        }
        finally
        {
            _saving = false;
        }
    }
}
