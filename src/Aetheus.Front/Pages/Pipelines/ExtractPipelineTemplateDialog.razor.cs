// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Pipelines;

public partial class ExtractPipelineTemplateDialog
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Parameter] public int PipelineId { get; set; }
    [Parameter] public string SuggestedName { get; set; } = string.Empty;
    [Parameter] public string SourceYaml { get; set; } = string.Empty;
    private Model _model = new();
    private (int PipelineId, string SuggestedName, string SourceYaml)? _initializedKey;
    private bool _saving;

    protected override void OnParametersSet()
    {
        var key = (PipelineId, SuggestedName, SourceYaml);
        if (_initializedKey == key) return;
        _initializedKey = key;
        _model = new Model
        {
            TemplateName = SuggestedName,
            TemplateYamlContent = SourceYaml,
            RewrittenPipelineYaml = BuildPinnedYaml(SuggestedName)
        };
    }

    private async Task SubmitAsync()
    {
        if (_saving) return;
        _saving = true;
        try
        {
            var result = await Api.ExtractPipelineTemplateAsync(PipelineId, new ExtractPipelineTemplateRequest
            {
                TemplateName = _model.TemplateName,
                Description = _model.Description,
                Category = _model.Category,
                TemplateYamlContent = _model.TemplateYamlContent,
                RewrittenPipelineYaml = _model.RewrittenPipelineYaml,
                RewritePipeline = _model.RewritePipeline
            });
            Dialog.Close(result is not null);
        }
        finally
        {
            _saving = false;
        }
    }

    private void Cancel() => Dialog.Close(false);

    private sealed class Model
    {
        [Required, StringLength(100)] public string TemplateName { get; set; } = string.Empty;
        [StringLength(500)] public string Description { get; set; } = string.Empty;
        [Required, StringLength(50)] public string Category { get; set; } = "Pipeline";
        [Required, StringLength(50_000)] public string TemplateYamlContent { get; set; } = string.Empty;
        [StringLength(50_000)] public string RewrittenPipelineYaml { get; set; } = string.Empty;
        public bool RewritePipeline { get; set; } = true;
    }

    private string BuildPinnedYaml(string templateName)
    {
        var escapedName = SuggestedName.Replace("'", "''", StringComparison.Ordinal);
        var escapedTemplate = templateName.Replace("'", "''", StringComparison.Ordinal);
        return $"name: '{escapedName}'\nextends: '{escapedTemplate}@1'\nstages: []\n";
    }
}
