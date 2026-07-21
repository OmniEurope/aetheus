// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Aetheus.Front.Services;

public sealed class YamlSerializationService
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .DisableAliases()
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults)
        .Build();

    public PipelineYamlDefinition? Parse(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml)) return null;

        try
        {
            return Deserializer.Deserialize<PipelineYamlDefinition>(yaml);
        }
        catch
        {
            return null;
        }
    }

    public string Serialize(PipelineYamlDefinition definition)
    {
        return Serializer.Serialize(definition).TrimEnd();
    }
}
