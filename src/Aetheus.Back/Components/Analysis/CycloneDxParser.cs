// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;

namespace Aetheus.Back.Components.Analysis;

public static class CycloneDxParser
{
    private const int MaxComponents = 200_000;

    public static IReadOnlyList<ParsedAnalysisComponent> Parse(string content, AnalysisReportFormat format)
    {
        return format switch
        {
            AnalysisReportFormat.CycloneDxJson => ParseJson(content),
            AnalysisReportFormat.CycloneDxXml => ParseXml(content),
            _ => throw new BadRequestException("The report is not a CycloneDX document.")
        };
    }

    private static IReadOnlyList<ParsedAnalysisComponent> ParseJson(string content)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 64 });
        }
        catch (JsonException ex)
        {
            throw new BadRequestException($"Invalid CycloneDX JSON report: {ex.Message}");
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("components", out var components)
                || components.ValueKind != JsonValueKind.Array)
                throw new BadRequestException("Invalid CycloneDX JSON report: components must be an array.");
            if (components.GetArrayLength() > MaxComponents)
                throw new BadRequestException($"CycloneDX report exceeds the maximum of {MaxComponents} components.");

            return components.EnumerateArray().Select(component => new ParsedAnalysisComponent
            {
                Name = RequiredString(component, "name", "CycloneDX component"),
                Version = ReadString(component, "version") ?? string.Empty,
                PackageUrl = ReadString(component, "purl"),
                ComponentType = ReadString(component, "type"),
                Licenses = ReadLicenses(component),
                Hash = ReadJsonHash(component),
                IsDirect = ReadString(component, "scope") == "required"
            }).ToList();
        }
    }

    private static IReadOnlyList<ParsedAnalysisComponent> ParseXml(string content)
    {
        System.Xml.Linq.XDocument document;
        try
        {
            document = SafeXml.LoadTrustedReport(content);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new BadRequestException($"Invalid CycloneDX XML report: {ex.Message}");
        }

        if (document.Root?.Name.LocalName != "bom")
            throw new BadRequestException("Invalid CycloneDX XML report: root element must be bom.");
        var components = document.Descendants().Where(element => element.Name.LocalName == "component").ToList();
        if (components.Count > MaxComponents)
            throw new BadRequestException($"CycloneDX report exceeds the maximum of {MaxComponents} components.");

        return components.Select(component => new ParsedAnalysisComponent
        {
            Name = RequiredElement(component, "name"),
            Version = ChildValue(component, "version") ?? string.Empty,
            PackageUrl = ChildValue(component, "purl"),
            ComponentType = component.Attribute("type")?.Value,
            Licenses = component.Descendants()
                .Where(element => (element.Name.LocalName is "id" or "name" or "expression")
                    && element.Ancestors().Any(ancestor => ancestor.Name.LocalName == "licenses"))
                .Select(element => element.Value.Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Hash = component.Descendants().FirstOrDefault(element => element.Name.LocalName == "hash")?.Value,
            IsDirect = string.Equals(ChildValue(component, "scope"), "required", StringComparison.OrdinalIgnoreCase)
        }).ToList();
    }

    private static IReadOnlyList<string> ReadLicenses(JsonElement component)
    {
        if (!component.TryGetProperty("licenses", out var licenses) || licenses.ValueKind != JsonValueKind.Array) return [];
        var values = new List<string>();
        foreach (var item in licenses.EnumerateArray())
        {
            var value = ReadString(item, "expression");
            if (value is null && item.TryGetProperty("license", out var license))
                value = ReadString(license, "id") ?? ReadString(license, "name");
            if (!string.IsNullOrWhiteSpace(value)) values.Add(value);
        }
        return values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? ReadJsonHash(JsonElement component)
    {
        if (!component.TryGetProperty("hashes", out var hashes) || hashes.ValueKind != JsonValueKind.Array) return null;
        var hash = hashes.EnumerateArray().FirstOrDefault();
        if (hash.ValueKind != JsonValueKind.Object) return null;
        var algorithm = ReadString(hash, "alg");
        var content = ReadString(hash, "content");
        return content is null ? null : string.IsNullOrWhiteSpace(algorithm) ? content : $"{algorithm}:{content}";
    }

    private static string RequiredString(JsonElement element, string property, string label)
    {
        var value = ReadString(element, property);
        return string.IsNullOrWhiteSpace(value)
            ? throw new BadRequestException($"{label} is missing {property}.")
            : value;
    }

    private static string? ReadString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string RequiredElement(System.Xml.Linq.XElement element, string name)
        => ChildValue(element, name) is { Length: > 0 } value
            ? value
            : throw new BadRequestException($"CycloneDX component is missing {name}.");

    private static string? ChildValue(System.Xml.Linq.XElement element, string name)
        => element.Elements().FirstOrDefault(child => child.Name.LocalName == name)?.Value.Trim();
}
