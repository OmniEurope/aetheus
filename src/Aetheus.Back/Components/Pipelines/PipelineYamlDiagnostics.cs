// SPDX-License-Identifier: EUPL-1.2
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization.NamingConventions;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// Names every key of a pipeline YAML that the running backend does not bind. The deserializer skips
/// such keys so that a definition written for a newer backend still runs (recette R2-041); this walk is
/// what keeps the skip visible, in the editor's validation result and in the run's warnings.
/// </summary>
internal static class PipelineYamlDiagnostics
{
    // Derived from the definitions the deserializer binds, with its own naming convention:
    // four hand-kept lists drifted from them, and the editor announced that honoured fields such as
    // confirm_minutes, reload_helper or deployed "will be ignored". The walk follows the property types,
    // so a nested block (isolation, strategy, a step's outputs...) is checked the same way.
    private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, Type>> KnownKeys = new();

    /// <summary>Returns the unknown-key warnings of <paramref name="yaml"/> (empty when it does not parse).</summary>
    public static List<string> UnknownPropertyWarnings(string yaml, ILogger logger)
    {
        var warnings = new List<string>();
        AppendUnknownPropertyWarnings(yaml, warnings, logger);
        return warnings;
    }

    public static void AppendUnknownPropertyWarnings(string yaml, List<string> warnings, ILogger logger)
    {
        try
        {
            using var reader = new StringReader(yaml);
            var yamlStream = new YamlStream();
            yamlStream.Load(reader);
            if (yamlStream.Documents.Count == 0 || yamlStream.Documents[0].RootNode is not YamlMappingNode root)
                return;

            AppendMappingWarnings(root, typeof(PipelineYamlDefinition), "top-level", warnings);
        }
        catch (Exception ex) when (ex is YamlDotNet.Core.YamlException or InvalidCastException or InvalidOperationException)
        {
            logger.LogDebug(ex, "YAML DOM walk for unknown-key warnings failed");
        }
    }

    private static void AppendMappingWarnings(YamlMappingNode node, Type type, string context, List<string> warnings)
    {
        var known = KnownKeys.GetOrAdd(type, KeysOf);
        foreach (var item in node.Children)
        {
            var key = Scalar(item.Key) ?? string.Empty;
            if (!known.TryGetValue(key, out var propertyType))
            {
                warnings.Add($"Unknown {context} property '{key}' will be ignored.");
                continue;
            }
            AppendValueWarnings(item.Value, propertyType, $"{context} > {key}", warnings);
        }
    }

    private static void AppendValueWarnings(YamlNode value, Type type, string context, List<string> warnings)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (value is YamlMappingNode mapping && IsDefinition(target))
        {
            AppendMappingWarnings(mapping, target, context, warnings);
            return;
        }
        if (value is not YamlSequenceNode sequence || ElementType(target) is not { } element)
            return;
        foreach (var child in sequence.Children.OfType<YamlMappingNode>())
            AppendMappingWarnings(child, element, ItemContext(element, child, context), warnings);
    }

    // The three levels people write most keep the short labels the editor has always shown.
    private static string ItemContext(Type element, YamlMappingNode item, string parentContext)
    {
        if (element == typeof(PipelineStageDefinition))
            return $"stage '{Scalar(item.Children.FirstOrDefault(k => Scalar(k.Key) == "name").Value) ?? "?"}'";
        if (element == typeof(PipelineJobDefinition)) return "job";
        if (element == typeof(PipelineStepDefinition)) return "step";
        return parentContext;
    }

    private static Type? ElementType(Type type)
    {
        if (type == typeof(string) || typeof(IDictionary).IsAssignableFrom(type)) return null;
        var enumerable = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            ? type
            : type.GetInterfaces().FirstOrDefault(candidate =>
                candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        var element = enumerable?.GetGenericArguments()[0];
        return element is not null && IsDefinition(element) ? element : null;
    }

    // Only the pipeline contract types are walked: a dictionary (variables, config_files) has free keys,
    // and a scalar has none.
    private static bool IsDefinition(Type type) =>
        type.IsClass && type != typeof(string)
        && !typeof(IEnumerable).IsAssignableFrom(type)
        && type.Namespace?.StartsWith("Aetheus.", StringComparison.Ordinal) == true;

    private static IReadOnlyDictionary<string, Type> KeysOf(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod is { IsPublic: true })
            .ToDictionary(
                property => UnderscoredNamingConvention.Instance.Apply(property.Name),
                property => property.PropertyType,
                StringComparer.Ordinal);

    private static string? Scalar(YamlNode? node) => (node as YamlScalarNode)?.Value;
}
