// SPDX-License-Identifier: EUPL-1.2
using System.Text;
using System.Text.Json;

namespace Aetheus.Front.Components.Shared;

/// <summary>
/// Renders an OTLP log body that is a <c>Microsoft.Extensions.Logging</c> message template
/// (<c>"Received HTTP response headers after {ElapsedMilliseconds}ms - {StatusCode}"</c>) with the
/// values the exporter sent alongside it as attributes. The exporter keeps the template as the body
/// on purpose (<c>IncludeFormattedMessage = false</c>), so without this the log viewer printed the
/// placeholders instead of the values.
/// </summary>
public static class LogMessageTemplate
{
    /// <summary>
    /// Replaces each <c>{Name}</c>, <c>{Name:format}</c> or <c>{Name,alignment}</c> hole with the
    /// attribute of that name. A hole without a matching attribute is kept as written, so a message
    /// is never shown with a value silently missing; <c>{{</c> and <c>}}</c> are literal braces.
    /// </summary>
    public static string Render(string body, string? attributesJson)
    {
        if (string.IsNullOrEmpty(body) || !body.Contains('{', StringComparison.Ordinal))
            return body;
        var values = ReadAttributes(attributesJson);
        if (values.Count == 0)
            return body;

        var output = new StringBuilder(body.Length + 32);
        for (var index = 0; index < body.Length; index++)
        {
            var current = body[index];
            if (current == '{' && index + 1 < body.Length && body[index + 1] == '{')
            {
                output.Append('{');
                index++;
                continue;
            }
            if (current == '}' && index + 1 < body.Length && body[index + 1] == '}')
            {
                output.Append('}');
                index++;
                continue;
            }
            if (current != '{')
            {
                output.Append(current);
                continue;
            }

            var close = body.IndexOf('}', index + 1);
            if (close < 0)
            {
                output.Append(body, index, body.Length - index);
                break;
            }

            var hole = body.Substring(index + 1, close - index - 1);
            var nameEnd = hole.IndexOfAny([':', ',']);
            var name = (nameEnd < 0 ? hole : hole[..nameEnd]).TrimStart('@', '$');
            if (values.TryGetValue(name, out var value))
                output.Append(value);
            else
                output.Append(body, index, close - index + 1);
            index = close;
        }
        return output.ToString();
    }

    private static Dictionary<string, string> ReadAttributes(string? attributesJson)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(attributesJson))
            return values;
        try
        {
            using var document = JsonDocument.Parse(attributesJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return values;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                values[property.Name] = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? string.Empty
                    : property.Value.GetRawText();
            }
        }
        catch (JsonException)
        {
            // Attributes are best effort: an unreadable set leaves the template as it was sent.
        }
        return values;
    }
}
