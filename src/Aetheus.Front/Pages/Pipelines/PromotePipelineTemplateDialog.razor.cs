// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Pipelines;

public partial class PromotePipelineTemplateDialog
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Parameter] public int PipelineId { get; set; }
    private PipelinePromotePreviewDto? _preview;
    private Model _model = new();
    private bool _loading = true;
    private bool _loadFailed;
    private bool _saving;

    protected override Task OnInitializedAsync() => LoadPreviewAsync();

    private async Task LoadPreviewAsync()
    {
        _loading = true;
        _loadFailed = false;
        _preview = null;
        try
        {
            _preview = await Api.GetPipelinePromotePreviewAsync(PipelineId);
            if (_preview is not null) _model.YamlContent = _preview.EffectivePipelineYaml;
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

    private async Task SubmitAsync()
    {
        if (_saving || _preview is null) return;
        _saving = true;
        try
        {
            var result = await Api.PromotePipelineTemplateAsync(PipelineId, new PromotePipelineTemplateRequest
            {
                ChangelogEntry = _model.ChangelogEntry,
                YamlContent = _model.YamlContent,
                RebaseSourcePipeline = _model.RebaseSourcePipeline
            });
            if (result is not null)
                Dialog.Close(true);
        }
        finally
        {
            _saving = false;
        }
    }

    private void Cancel() => Dialog.Close(false);

    private sealed class Model
    {
        [Required, StringLength(500)] public string ChangelogEntry { get; set; } = string.Empty;
        [Required, StringLength(50_000)] public string YamlContent { get; set; } = string.Empty;
        public bool RebaseSourcePipeline { get; set; }
    }
}
