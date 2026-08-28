// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Pipelines;

internal sealed class PipelineTemplateEditorCoordinator(ApiClient api)
{
    public Task<List<PipelineTemplateSummaryDto>> LoadTemplatesAsync() =>
        api.PipelineTemplates.GetPipelineTemplatesAsync();

    public async Task<string?> LoadBaseYamlAsync(
        string yaml, IReadOnlyCollection<PipelineTemplateSummaryDto> templates)
    {
        var reference = PipelineTemplateReferenceHelper.Parse(yaml);
        if (reference is null) return null;
        var summary = FindSummary(reference.Name, templates);
        if (summary is null) return null;
        var template = await api.PipelineTemplates.GetPipelineTemplateAsync(summary.Id);
        if (template is null) return null;
        var version = reference.Version ?? template.Version;
        return await api.PipelineTemplates.ResolvePipelineTemplateAsync(summary.Id, version);
    }

    public async Task<PipelineTemplateSelection?> SelectAsync(int templateId, string pipelineName)
    {
        var template = await api.PipelineTemplates.GetPipelineTemplateAsync(templateId);
        if (template is null) return null;
        var serializer = new YamlSerializationService();
        var templateDefinition = serializer.Parse(template.YamlContent);
        if (templateDefinition is null) return null;
        var resolvedBaseYaml = await api.PipelineTemplates.ResolvePipelineTemplateAsync(template.Id, template.Version);
        if (resolvedBaseYaml is null) return null;
        var yaml = serializer.Serialize(new PipelineYamlDefinition
        {
            Name = string.IsNullOrWhiteSpace(pipelineName) ? template.Name : pipelineName,
            Extends = $"{template.Name}@{template.Version}",
            Stages = []
        });
        return new PipelineTemplateSelection(yaml, resolvedBaseYaml, templateDefinition.Parameters);
    }

    public static string ApplyParameters(
        PipelineTemplateSelection selection,
        IReadOnlyDictionary<string, string> values)
    {
        var serializer = new YamlSerializationService();
        var definition = serializer.Parse(selection.Yaml)
            ?? throw new InvalidOperationException("The generated pipeline YAML could not be parsed.");
        var parameters = selection.Parameters
            .Select(parameter => parameter with
            {
                Default = values.TryGetValue(parameter.Name, out var value) ? value : parameter.Default
            })
            .ToList();
        return serializer.Serialize(definition with { Parameters = parameters });
    }

    public string? Pin(string yaml, IReadOnlyCollection<PipelineTemplateSummaryDto> templates)
    {
        var reference = PipelineTemplateReferenceHelper.Parse(yaml);
        if (reference is not { Version: null }) return null;
        var summary = FindSummary(reference.Name, templates);
        return summary is null
            ? null
            : PipelineTemplateReferenceHelper.Pin(yaml, summary.Name, summary.Version);
    }

    public async Task<PipelineFleetItemDto?> FindFleetItemAsync(int pipelineId)
    {
        try { return await api.Pipelines.GetPipelineFleetItemAsync(pipelineId); }
        catch (HttpRequestException) { return null; }
    }

    private static PipelineTemplateSummaryDto? FindSummary(
        string name, IEnumerable<PipelineTemplateSummaryDto> templates) =>
        templates.FirstOrDefault(template =>
            string.Equals(template.Name, name, StringComparison.OrdinalIgnoreCase));
}
