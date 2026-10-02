// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Pipelines;

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
        var templateYaml = version == template.Version
            ? template.YamlContent
            : (await api.PipelineTemplates.GetPipelineTemplateVersionAsync(summary.Id, version))?.YamlContent;
        var values = ParameterValues(templateYaml, yaml);
        return values is null
            ? null
            : await api.PipelineTemplates.ResolvePipelineTemplateAsync(summary.Id, version, values);
    }

    /// <summary>
    /// Recette R-490: the values the template is resolved with, its own defaults overridden by the
    /// defaults the pipeline declares for the same parameters. The call used to send nothing, so a
    /// template with a required parameter answered 400 ("Parameter 'revision' is required.") even when
    /// the pipeline extending it gave that parameter a value. Null when a required parameter has no
    /// value from either side: there is nothing to resolve yet, and no request is sent.
    /// </summary>
    internal static Dictionary<string, string>? ParameterValues(string? templateYaml, string? pipelineYaml)
    {
        var serializer = new YamlSerializationService();
        var declared = templateYaml is null ? [] : serializer.Parse(templateYaml)?.Parameters ?? [];
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var parameter in declared.Where(parameter => parameter.Default is not null))
            values[parameter.Name] = parameter.Default!;
        var overrides = pipelineYaml is null ? [] : serializer.Parse(pipelineYaml)?.Parameters ?? [];
        foreach (var parameter in overrides.Where(parameter => parameter.Default is not null))
            values[parameter.Name] = parameter.Default!;
        return declared.Any(parameter => parameter.Required && !values.ContainsKey(parameter.Name)) ? null : values;
    }

    public async Task<PipelineTemplateSelection?> SelectAsync(int templateId, string pipelineName)
    {
        var template = await api.PipelineTemplates.GetPipelineTemplateAsync(templateId);
        if (template is null) return null;
        var serializer = new YamlSerializationService();
        var templateDefinition = serializer.Parse(template.YamlContent);
        if (templateDefinition is null) return null;
        // A required parameter without a default has no value before the user fills it in: the base is
        // then the template as written, its parameter references still in place.
        var values = ParameterValues(template.YamlContent, null);
        var resolvedBaseYaml = values is null
            ? template.YamlContent
            : await api.PipelineTemplates.ResolvePipelineTemplateAsync(template.Id, template.Version, values);
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
