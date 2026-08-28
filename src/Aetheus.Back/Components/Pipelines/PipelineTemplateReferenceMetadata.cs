// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Pipelines;

internal static class PipelineTemplateReferenceMetadata
{
    public static void Apply(Pipeline pipeline, string yaml)
    {
        var reference = Parse(yaml);
        pipeline.TemplateReferenceName = reference?.Name;
        pipeline.TemplateReferenceVersion = reference?.Version;
    }

    public static (string Name, int? Version)? Parse(string yaml)
    {
        var definition = YamlParsingHelper.Deserializer.Deserialize<PipelineYamlDefinition>(yaml);
        if (string.IsNullOrWhiteSpace(definition?.Extends)) return null;

        var value = definition.Extends.Trim();
        var separator = value.LastIndexOf('@');
        return separator > 0
            && int.TryParse(value[(separator + 1)..], out var version)
            && version > 0
                ? (value[..separator], version)
                : (value, null);
    }
}
