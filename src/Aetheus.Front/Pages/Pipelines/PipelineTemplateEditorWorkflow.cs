// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Pipelines;

/// <summary>
/// Coordinates the template-specific dialogs and API lookups used by the pipeline editor.
/// </summary>
internal sealed class PipelineTemplateEditorWorkflow(
    ApiClient api,
    DialogService dialog,
    IStringLocalizer<AppStrings> localizer)
{
    private readonly PipelineTemplateEditorCoordinator _editor = new(api);

    public Task<List<PipelineTemplateSummaryDto>> LoadTemplatesAsync() =>
        _editor.LoadTemplatesAsync();

    public Task<string?> LoadBaseYamlAsync(
        string yaml,
        IReadOnlyCollection<PipelineTemplateSummaryDto> templates) =>
        _editor.LoadBaseYamlAsync(yaml, templates);

    public string? Pin(string yaml, IReadOnlyCollection<PipelineTemplateSummaryDto> templates) =>
        _editor.Pin(yaml, templates);

    public async Task<(string Yaml, string BaseYaml)?> SelectAsync(int templateId, string pipelineName)
    {
        var selection = await _editor.SelectAsync(templateId, pipelineName);
        if (selection is null) return null;
        if (selection.Parameters.Count == 0)
            return (selection.Yaml, selection.BaseYaml);

        var parameters = selection.Parameters.Select(parameter => new PipelineRunParameterDto
        {
            Name = parameter.Name,
            DisplayName = parameter.DisplayName ?? parameter.Name,
            Type = parameter.Type,
            Default = parameter.Default,
            Required = parameter.Required,
            Description = parameter.Description,
            AllowedValues = parameter.AllowedValues
        }).ToList();
        var result = await dialog.OpenAsync<RunParametersDialog>(
            localizer["TemplateParameters"],
            new Dictionary<string, object?>
            {
                ["Parameters"] = parameters,
                ["SubmitText"] = localizer["Apply"].Value,
                ["SubmitIcon"] = "check"
            },
            new DialogOptions { Width = "35rem" });
        return result is Dictionary<string, string> values
            ? (PipelineTemplateEditorCoordinator.ApplyParameters(selection, values), selection.BaseYaml)
            : null;
    }

    public async Task<bool> ExtractAsync(int pipelineId, string name, string yaml) =>
        await dialog.OpenAsync<ExtractPipelineTemplateDialog>(
            localizer["ExtractAsTemplate"],
            new Dictionary<string, object?>
            {
                ["PipelineId"] = pipelineId,
                ["SuggestedName"] = name,
                ["SourceYaml"] = yaml
            },
            new DialogOptions { Width = "40rem" }) is true;

    public async Task<bool> PromoteAsync(int pipelineId) =>
        await dialog.OpenAsync<PromotePipelineTemplateDialog>(
            localizer["PromoteToTemplate"],
            new Dictionary<string, object?> { ["PipelineId"] = pipelineId },
            new DialogOptions { Width = "80rem" }) is true;

    public async Task<bool> UpdateAsync(int pipelineId, int targetVersion) =>
        await dialog.OpenAsync<PipelineFleetUpdateDialog>(
            localizer["UpdatePipelineTemplate"],
            new Dictionary<string, object?>
            {
                ["PipelineId"] = pipelineId,
                ["TargetVersion"] = targetVersion
            },
            new DialogOptions { Width = "80rem" }) is true;

    public async Task<PipelineFleetItemDto?> FindFleetItemAsync(int pipelineId)
    {
        try
        {
            return await api.GetPipelineFleetItemAsync(pipelineId);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
}
