// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Pipelines;

public partial class PipelineFleetUpdateDialog
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Parameter] public int PipelineId { get; set; }
    [Parameter] public int TargetVersion { get; set; }

    private PipelineFleetUpdatePreviewDto? _preview;
    private bool _loading = true;
    private bool _loadFailed;
    private bool _saving;
    private bool _acknowledged;

    protected override Task OnInitializedAsync() => LoadPreviewAsync();

    private async Task LoadPreviewAsync()
    {
        _loading = true;
        _loadFailed = false;
        _preview = null;
        try
        {
            _preview = await Api.PreviewPipelineFleetUpdateAsync(PipelineId, TargetVersion);
            _loadFailed = _preview is null;
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

    private async Task ConfirmAsync()
    {
        if (_saving || _preview is null) return;
        _saving = true;
        try
        {
            var updated = await Api.ApplyPipelineFleetUpdateAsync(PipelineId, new PipelineFleetUpdateRequest
            {
                TargetVersion = TargetVersion,
                AcknowledgeOrphanOverrides = _acknowledged,
                ExpectedSourceYamlHash = _preview.SourceYamlHash
            });
            if (updated is not null)
                Dialog.Close(true);
        }
        finally
        {
            _saving = false;
        }
    }

    private void Cancel() => Dialog.Close(false);
}
