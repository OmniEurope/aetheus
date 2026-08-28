// SPDX-License-Identifier: EUPL-1.2
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
using NuGet.Packaging;
using NuGet.Versioning;

namespace Aetheus.Back.Components.PackageRegistry;

internal sealed class NuGetPackageInspector
{
    private const int MaxArchiveEntries = 4096;
    private const long MaxDecompressedBytes = 512L * 1024 * 1024;
    private const int MaxCompressionRatio = 200;

    private static readonly Regex PackageIdPattern = new(
        "^[A-Za-z0-9_][A-Za-z0-9_.-]{0,99}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<NuGetPackageMetadata> InspectAsync(Stream content, CancellationToken ct)
    {
        try
        {
            return await InspectCoreAsync(content, ct).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            throw new BadRequestException("The NuGet package is not a valid archive.");
        }
        catch (XmlException)
        {
            throw new BadRequestException("The NuGet package manifest is not valid XML.");
        }
    }

    private static async Task<NuGetPackageMetadata> InspectCoreAsync(Stream content, CancellationToken ct)
    {
        if (!content.CanSeek)
            throw new BadRequestException("The NuGet package stream must be seekable.");

        content.Position = 0;
        string manifest;
        using (var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true))
        {
            ValidateArchiveBounds(archive);
            var entries = archive.Entries
                .Where(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (entries.Count != 1 || entries[0].Length > 1024 * 1024)
                throw new BadRequestException("The NuGet package must contain exactly one bounded .nuspec manifest.");

            await using var manifestStream = entries[0].Open();
            using var reader = new StreamReader(manifestStream);
            manifest = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        }

        content.Position = 0;
        using var packageReader = new PackageArchiveReader(content, leaveStreamOpen: true);
        var nuspec = await packageReader.GetNuspecReaderAsync(ct).ConfigureAwait(false);
        var id = nuspec.GetId();
        var version = nuspec.GetVersion();
        ValidatePackageId(id);
        if (version is null)
            throw new BadRequestException("The NuGet package manifest has no valid version.");

        content.Position = 0;
        var dependencyGroups = nuspec.GetDependencyGroups()
            .Select(group => new NuGetDependencyGroup(
                group.TargetFramework.GetShortFolderName(),
                group.Packages
                    .Select(package => new NuGetPackageDependency(
                        package.Id,
                        package.VersionRange.ToNormalizedString()))
                    .ToList()))
            .ToList();
        return new NuGetPackageMetadata(
            id,
            id.ToLowerInvariant(),
            version.ToNormalizedString(),
            version.ToNormalizedString().ToLowerInvariant(),
            version.IsPrerelease,
            Truncate(nuspec.GetDescription(), 1000),
            manifest,
            Truncate(nuspec.GetAuthors(), 500),
            Truncate(nuspec.GetTags(), 500),
            dependencyGroups);
    }

    public static NuGetVersion ParseVersion(string value)
    {
        if (!NuGetVersion.TryParse(value, out var version))
            throw new BadRequestException("Invalid NuGet package version.");
        return version;
    }

    public static void ValidatePackageId(string value)
    {
        if (!PackageIdPattern.IsMatch(value)
            || value.Contains("..", StringComparison.Ordinal)
            || value.Contains("--", StringComparison.Ordinal))
            throw new BadRequestException("Invalid NuGet package ID.");
    }

    private static void ValidateArchiveBounds(ZipArchive archive)
    {
        if (archive.Entries.Count > MaxArchiveEntries)
            throw new BadRequestException("The NuGet package contains too many archive entries.");

        long decompressedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.Length > MaxDecompressedBytes - decompressedBytes)
                throw new BadRequestException("The NuGet package expands beyond the registry limit.");
            decompressedBytes += entry.Length;

            if (entry.Length > 1024 * 1024
                && entry.CompressedLength > 0
                && entry.Length / entry.CompressedLength > MaxCompressionRatio)
                throw new BadRequestException("The NuGet package has an unsafe compression ratio.");
        }
    }

    private static string? Truncate(string? value, int maxLength)
        => string.IsNullOrWhiteSpace(value) ? null : value[..Math.Min(value.Length, maxLength)];
}

internal sealed record NuGetPackageMetadata(
    string Name,
    string NormalizedName,
    string Version,
    string NormalizedVersion,
    bool IsPrerelease,
    string? Description,
    string Manifest,
    string? Authors,
    string? Tags,
    IReadOnlyList<NuGetDependencyGroup> DependencyGroups);
