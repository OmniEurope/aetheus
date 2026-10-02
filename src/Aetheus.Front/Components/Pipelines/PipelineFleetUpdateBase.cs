// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Pipelines;

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
    protected bool _notAvailable;
    protected bool _forbidden;
    protected bool _saving;
    protected bool _acknowledged;

    protected async Task LoadPreviewAsync()
    {
        _loading = true;
        _loadFailed = false;
        _notAvailable = false;
        _forbidden = false;
        _preview = null;
        try
        {
            var outcome = await Api.Pipelines.PreviewPipelineFleetUpdateAsync(PipelineId, TargetVersion)
                .ConfigureAwait(false);
            _preview = outcome.Value;
            _notAvailable = outcome.NotFound;
            _forbidden = outcome.StatusCode == System.Net.HttpStatusCode.Forbidden;
            _loadFailed = _preview is null && !_notAvailable && !_forbidden;
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
