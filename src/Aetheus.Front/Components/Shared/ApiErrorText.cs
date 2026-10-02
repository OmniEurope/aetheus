// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;

namespace Aetheus.Front.Components.Shared;

/// <summary>
/// Reads the human reason out of a refused request's body, whatever shape the endpoint wrote it in:
/// a validation result (<c>errors</c> as a list), the <c>ErrorHandlingMiddleware</c> error object
/// (<c>message</c>), a ProblemDetails (<c>detail</c>, then its <c>errors</c> dictionary, then
/// <c>title</c>), a JSON string, or plain text. Recette R2-039: a refusal must reach the user as text,
/// never as an exception, so nothing here throws on an unexpected body; it returns null instead.
/// </summary>
internal static class ApiErrorText
{
    public static string? Read(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;
        var text = payload.Trim();
        if (text[0] is not ('{' or '[' or '"')) return text;
        try
        {
            using var document = JsonDocument.Parse(text);
            return FromElement(document.RootElement);
        }
        catch (JsonException)
        {
            return text;
        }
    }

    private static string? FromElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => NonBlank(element.GetString()),
        JsonValueKind.Array => JoinStrings(element),
        JsonValueKind.Object => FromObject(element),
        _ => null
    };

    private static string? FromObject(JsonElement root)
    {
        var errors = Property(root, "errors");
        if (errors is { ValueKind: JsonValueKind.Array } list && JoinStrings(list) is { } listed)
            return listed;
        if (StringProperty(root, "message") is { } message) return message;
        if (StringProperty(root, "detail") is { } detail) return detail;
        if (errors is { ValueKind: JsonValueKind.Object } byField)
        {
            var fieldErrors = JoinStrings(byField.EnumerateObject().Select(field => field.Value));
            if (fieldErrors is not null) return fieldErrors;
        }
        return StringProperty(root, "title");
    }

    private static JsonElement? Property(JsonElement root, string name)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }
        return null;
    }

    private static string? StringProperty(JsonElement root, string name) =>
        Property(root, name) is { ValueKind: JsonValueKind.String } value ? NonBlank(value.GetString()) : null;

    private static string? JoinStrings(JsonElement array) => JoinStrings([array]);

    // Strings directly in the given values, or one level down when a value is itself a list of strings
    // (the per-field arrays of a ProblemDetails "errors" dictionary).
    private static string? JoinStrings(IEnumerable<JsonElement> values)
    {
        var parts = values
            .SelectMany(value => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : Enumerable.Repeat(value, 1))
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .OfType<string>()
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    private static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
