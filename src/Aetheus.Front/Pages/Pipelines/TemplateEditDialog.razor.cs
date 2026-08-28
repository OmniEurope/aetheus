// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Pipelines;

public partial class TemplateEditDialog
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;

    [Parameter] public PipelineTemplateDto? Template { get; set; }

    private TemplateFormModel _model = new();
    private bool IsEdit => Template is not null;

    protected override void OnParametersSet()
    {
        if (Template is not null)
        {
            _model = new TemplateFormModel
            {
                Name = Template.Name,
                Description = Template.Description,
                Category = Template.Category,
                YamlContent = Template.YamlContent,
                ChangelogEntry = string.Empty
            };
        }
    }

    private async Task OnSubmit()
    {
        PipelineTemplateDto? saved;
        if (IsEdit)
        {
            saved = await Api.PipelineTemplates.UpdatePipelineTemplateAsync(Template!.Id, new UpdatePipelineTemplateRequest
            {
                Name = _model.Name,
                Description = _model.Description,
                Category = _model.Category,
                YamlContent = _model.YamlContent,
                ChangelogEntry = _model.ChangelogEntry
            });
        }
        else
        {
            saved = await Api.PipelineTemplates.CreatePipelineTemplateAsync(new CreatePipelineTemplateRequest
            {
                Name = _model.Name,
                Description = _model.Description,
                Category = _model.Category,
                YamlContent = _model.YamlContent,
                ChangelogEntry = _model.ChangelogEntry
            });
        }
        if (saved is not null)
        {
            Toast.Success(IsEdit ? "Saved" : "Created", "Saved");
            Dialog.Close(true);
        }
        else
        {
            Toast.Error("Error", "SaveFailed");
        }
    }

    private void OnCancel() => Dialog.Close(false);

    private sealed class TemplateFormModel
    {
        [Required, StringLength(100)]
        public string Name { get; set; } = string.Empty;
        [StringLength(500)]
        public string Description { get; set; } = string.Empty;
        [Required, StringLength(50)]
        public string Category { get; set; } = string.Empty;
        [Required, StringLength(50_000)]
        public string YamlContent { get; set; } = string.Empty;
        [Required, StringLength(500)]
        public string ChangelogEntry { get; set; } = string.Empty;
    }
}
