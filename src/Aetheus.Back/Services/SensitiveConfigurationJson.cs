// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aetheus.Back.Services;

internal static class SensitiveConfigurationJson
{
    private const string Mask = "***";

    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "url",
        "webhookUrl",
        "webhook_url",
        "secret",
        "token",
        "accessToken",
        "pat",
        "password"
    };

    internal static string MaskSecrets(string configurationJson)
    {
        try
        {
            var root = JsonNode.Parse(configurationJson);
            if (root is null) return "{}";
            MaskNode(root);
            return root.ToJsonString();
        }
        catch (JsonException)
        {
            return "{}";
        }
    }

    internal static string RestoreMaskedSecrets(string existingJson, string requestedJson)
    {
        try
        {
            var existing = JsonNode.Parse(existingJson);
            var requested = JsonNode.Parse(requestedJson);
            if (existing is null || requested is null) return requestedJson;

            RestoreNode(existing, requested);
            return requested.ToJsonString();
        }
        catch (JsonException)
        {
            return requestedJson;
        }
    }

    private static void MaskNode(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToList())
            {
                if (SensitiveKeys.Contains(property.Key))
                    obj[property.Key] = Mask;
                else if (property.Value is not null)
                    MaskNode(property.Value);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
                if (item is not null) MaskNode(item);
        }
    }

    private static void RestoreNode(JsonNode existing, JsonNode requested)
    {
        if (existing is JsonObject existingObject && requested is JsonObject requestedObject)
            RestoreObject(existingObject, requestedObject);
        else if (existing is JsonArray existingArray && requested is JsonArray requestedArray)
            RestoreArray(existingArray, requestedArray);
    }

    private static void RestoreObject(JsonObject existing, JsonObject requested)
    {
        foreach (var property in requested.ToList())
        {
            var existingProperty = existing.FirstOrDefault(
                candidate => string.Equals(candidate.Key, property.Key, StringComparison.OrdinalIgnoreCase));
            if (existingProperty.Key is null) continue;

            if (IsMaskedSecret(property))
                requested[property.Key] = existingProperty.Value?.DeepClone();
            else if (property.Value is not null && existingProperty.Value is not null)
                RestoreNode(existingProperty.Value, property.Value);
        }
    }

    private static bool IsMaskedSecret(KeyValuePair<string, JsonNode?> property) =>
        SensitiveKeys.Contains(property.Key)
        && property.Value is JsonValue requestedValue
        && requestedValue.TryGetValue<string>(out var value)
        && value == Mask;

    private static void RestoreArray(JsonArray existing, JsonArray requested)
    {
        for (var index = 0; index < Math.Min(existing.Count, requested.Count); index++)
        {
            if (existing[index] is not null && requested[index] is not null)
            {
                RestoreNode(existing[index]!, requested[index]!);
            }
        }
    }
}
