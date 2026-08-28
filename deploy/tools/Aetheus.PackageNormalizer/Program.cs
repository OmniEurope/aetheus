// SPDX-License-Identifier: EUPL-1.2
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

const string CorePropertiesDirectory = "package/services/metadata/core-properties/";
const string CanonicalCorePropertiesPath = CorePropertiesDirectory + "core.psmdcp";
DateTimeOffset canonicalTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: Aetheus.PackageNormalizer <package.nupkg> [package.nupkg ...]");
    return 2;
}

foreach (string argument in args)
{
    string packagePath = Path.GetFullPath(argument);
    if (!packagePath.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)
        || !File.Exists(packagePath))
    {
        Console.Error.WriteLine($"A NuGet package was not found: {packagePath}");
        return 2;
    }

    string temporaryPath = packagePath + ".normalized";
    try
    {
        using FileStream inputStream = File.OpenRead(packagePath);
        using ZipArchive inputArchive = new(inputStream, ZipArchiveMode.Read, leaveOpen: false);
        ZipArchiveEntry? corePropertiesEntry = inputArchive.Entries.SingleOrDefault(
            entry => entry.FullName.StartsWith(CorePropertiesDirectory, StringComparison.Ordinal)
                     && entry.FullName.EndsWith(".psmdcp", StringComparison.OrdinalIgnoreCase));

        if (corePropertiesEntry is null)
        {
            Console.Error.WriteLine($"NuGet core properties are missing from {packagePath}.");
            return 3;
        }

        string originalCorePropertiesPath = corePropertiesEntry.FullName;
        List<PackageEntry> entries = [];
        foreach (ZipArchiveEntry entry in inputArchive.Entries)
        {
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                continue;
            }

            using Stream entryStream = entry.Open();
            using MemoryStream content = new();
            entryStream.CopyTo(content);
            string targetPath = string.Equals(
                entry.FullName,
                originalCorePropertiesPath,
                StringComparison.Ordinal)
                ? CanonicalCorePropertiesPath
                : entry.FullName;
            byte[] bytes = content.ToArray();

            if (string.Equals(entry.FullName, "_rels/.rels", StringComparison.Ordinal))
            {
                XDocument relationships = XDocument.Parse(
                    Encoding.UTF8.GetString(bytes),
                    LoadOptions.None);
                XElement coreRelationship = relationships
                    .Descendants()
                    .Single(element => element.Name.LocalName == "Relationship"
                                       && element.Attribute("Type")?.Value.EndsWith(
                                           "/metadata/core-properties",
                                           StringComparison.Ordinal) == true);
                coreRelationship.SetAttributeValue(
                    "Target",
                    "/" + CanonicalCorePropertiesPath);
                coreRelationship.SetAttributeValue("Id", "RCoreProperties");
                relationships.Declaration = new XDeclaration("1.0", "utf-8", null);
                string canonicalRelationships =
                    relationships.Declaration + "\n"
                    + relationships.ToString(SaveOptions.DisableFormatting);
                bytes = Encoding.UTF8.GetBytes(canonicalRelationships);
            }

            entries.Add(new PackageEntry(targetPath, bytes));
        }

        inputArchive.Dispose();
        inputStream.Dispose();

        using (FileStream outputStream = new(
                   temporaryPath,
                   FileMode.Create,
                   FileAccess.ReadWrite,
                   FileShare.None))
        using (ZipArchive outputArchive = new(outputStream, ZipArchiveMode.Create, leaveOpen: false))
        {
            foreach (PackageEntry entry in entries.OrderBy(item => item.Path, StringComparer.Ordinal))
            {
                ZipArchiveEntry outputEntry = outputArchive.CreateEntry(
                    entry.Path,
                    CompressionLevel.SmallestSize);
                outputEntry.LastWriteTime = canonicalTimestamp;
                using Stream outputEntryStream = outputEntry.Open();
                outputEntryStream.Write(entry.Content);
            }
        }

        File.Move(temporaryPath, packagePath, overwrite: true);
        Console.WriteLine($"Normalized {packagePath}");
    }
    finally
    {
        if (File.Exists(temporaryPath))
        {
            File.Delete(temporaryPath);
        }
    }
}

return 0;

internal sealed record PackageEntry(string Path, byte[] Content);
