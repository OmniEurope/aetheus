// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;

namespace Aetheus.Back.Components.Analysis;

public static class SpdxJsonParser
{
    private const int MaxPackages = 200_000;

    public static IReadOnlyList<ParsedAnalysisComponent> Parse(string content)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 64 });
        }
        catch (JsonException ex)
        {
            throw new BadRequestException($"Invalid SPDX JSON report: {ex.Message}");
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("packages", out var packages) || packages.ValueKind != JsonValueKind.Array)
                throw new BadRequestException("Invalid SPDX JSON report: packages must be an array.");
            if (packages.GetArrayLength() > MaxPackages)
                throw new BadRequestException($"SPDX report exceeds the maximum of {MaxPackages} packages.");

            return packages.EnumerateArray().Select(package => new ParsedAnalysisComponent
            {
                Name = RequiredString(package, "name"),
                Version = ReadString(package, "versionInfo") ?? string.Empty,
                PackageUrl = ReadPackageUrl(package),
                ComponentType = ReadString(package, "primaryPackagePurpose"),
                Licenses = ReadLicenses(package),
                Hash = ReadChecksum(package)
            }).ToList();
        }
    }

    private static string? ReadPackageUrl(JsonElement package)
    {
        if (!package.TryGetProperty("externalRefs", out var refs) || refs.ValueKind != JsonValueKind.Array) return null;
        foreach (var reference in refs.EnumerateArray())
        {
            var locator = ReadString(reference, "referenceLocator");
            if (locator?.StartsWith("pkg:", StringComparison.OrdinalIgnoreCase) == true) return locator;
        }
        return null;
    }

    private static IReadOnlyList<string> ReadLicenses(JsonElement package)
    {
        return new[] { ReadString(package, "licenseConcluded"), ReadString(package, "licenseDeclared") }
            .Where(value => !string.IsNullOrWhiteSpace(value) && value != "NOASSERTION" && value != "NONE")
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? ReadChecksum(JsonElement package)
    {
        if (!package.TryGetProperty("checksums", out var checksums) || checksums.ValueKind != JsonValueKind.Array) return null;
        var checksum = checksums.EnumerateArray().FirstOrDefault();
        if (checksum.ValueKind != JsonValueKind.Object) return null;
        var algorithm = ReadString(checksum, "algorithm");
        var value = ReadString(checksum, "checksumValue");
        return value is null ? null : string.IsNullOrWhiteSpace(algorithm) ? value : $"{algorithm}:{value}";
    }

    private static string RequiredString(JsonElement element, string property)
    {
        var value = ReadString(element, property);
        return string.IsNullOrWhiteSpace(value)
            ? throw new BadRequestException($"SPDX package is missing {property}.")
            : value;
    }

    private static string? ReadString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
