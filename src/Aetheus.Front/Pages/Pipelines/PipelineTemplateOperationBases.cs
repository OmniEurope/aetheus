// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Pipelines;

public abstract class PipelineTemplateExtractionBase : ComponentBase
{
    [Inject] protected ApiClient Api { get; set; } = default!;
    [Inject] protected IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter] public int PipelineId { get; set; }

    protected PipelineTemplateExtractionModel _model = new();
    protected bool _saving;

    protected async Task<PipelineTemplateDto?> ExtractAsync()
    {
        if (_saving)
            return null;

        _saving = true;
        try
        {
            return await Api.PipelineTemplates.ExtractPipelineTemplateAsync(PipelineId, _model.ToRequest())
                .ConfigureAwait(false);
        }
        finally
        {
            _saving = false;
        }
    }
}

public sealed class PipelineTemplateExtractionModel
{
    [Required, StringLength(100)] public string TemplateName { get; set; } = string.Empty;
    [StringLength(500)] public string Description { get; set; } = string.Empty;
    [Required, StringLength(50)] public string Category { get; set; } = "Pipeline";
    [Required, StringLength(50_000)] public string TemplateYamlContent { get; set; } = string.Empty;
    [StringLength(50_000)] public string RewrittenPipelineYaml { get; set; } = string.Empty;
    public bool RewritePipeline { get; set; } = true;

    public ExtractPipelineTemplateRequest ToRequest() => new()
    {
        TemplateName = TemplateName,
        Description = Description,
        Category = Category,
        TemplateYamlContent = TemplateYamlContent,
        RewrittenPipelineYaml = RewrittenPipelineYaml,
        RewritePipeline = RewritePipeline
    };

    public static string BuildPinnedYaml(string pipelineName, string templateName)
    {
        var escapedName = pipelineName.Replace("'", "''", StringComparison.Ordinal);
        var escapedTemplate = templateName.Replace("'", "''", StringComparison.Ordinal);
        return $"name: '{escapedName}'\nextends: '{escapedTemplate}@1'\nstages: []\n";
    }
}

public abstract class PipelineTemplatePromotionBase : ComponentBase
{
    [Inject] protected ApiClient Api { get; set; } = default!;
    [Inject] protected IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter] public int PipelineId { get; set; }

    protected PipelinePromotePreviewDto? _preview;
    protected PipelineTemplatePromotionModel _model = new();
    protected bool _loading = true;
    protected bool _loadFailed;
    protected bool _saving;

    protected async Task LoadPreviewAsync()
    {
        _loading = true;
        _loadFailed = false;
        _preview = null;
        try
        {
            _preview = await Api.Pipelines.GetPipelinePromotePreviewAsync(PipelineId).ConfigureAwait(false);
            _loadFailed = _preview is null;
            if (_preview is not null)
            {
                _model.YamlContent = _preview.EffectivePipelineYaml;
                await OnPreviewLoadedAsync(_preview).ConfigureAwait(false);
            }
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

    protected virtual Task OnPreviewLoadedAsync(PipelinePromotePreviewDto preview) => Task.CompletedTask;

    protected async Task<PipelineTemplateDto?> PromoteAsync()
    {
        if (_saving || _preview is null)
            return null;

        _saving = true;
        try
        {
            return await Api.PipelineTemplates.PromotePipelineTemplateAsync(PipelineId, _model.ToRequest())
                .ConfigureAwait(false);
        }
        finally
        {
            _saving = false;
        }
    }
}

public sealed class PipelineTemplatePromotionModel
{
    [Required, StringLength(500)] public string ChangelogEntry { get; set; } = string.Empty;
    [Required, StringLength(50_000)] public string YamlContent { get; set; } = string.Empty;
    public bool RebaseSourcePipeline { get; set; }

    public PromotePipelineTemplateRequest ToRequest() => new()
    {
        ChangelogEntry = ChangelogEntry,
        YamlContent = YamlContent,
        RebaseSourcePipeline = RebaseSourcePipeline
    };
}
