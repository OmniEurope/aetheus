// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Operations;

internal static class BackupArchivePathHelpers
{
    internal static bool TryGetSourceRoot(string entryName, out string sourceRoot)
    {
        const int prefixLength = 6; // "files/"
        var separator = entryName.IndexOf('/', prefixLength);
        if (!entryName.StartsWith("files/", StringComparison.Ordinal) || separator <= prefixLength)
        {
            sourceRoot = string.Empty;
            return false;
        }
        sourceRoot = entryName[..separator];
        return true;
    }

    internal static bool HasValidMetadata(long? lastWriteTimeUtcTicks, int? unixMode)
    {
        if (lastWriteTimeUtcTicks is null
            || lastWriteTimeUtcTicks.Value < DateTime.MinValue.Ticks
            || lastWriteTimeUtcTicks.Value > DateTime.MaxValue.Ticks)
        {
            return false;
        }
        return OperatingSystem.IsWindows() ? unixMode is null : unixMode is >= 0 and <= 0xFFF;
    }

    internal static string GetDirectoryExtractionPath(string extractionRoot, BackupBundleDirectory directory)
    {
        var entryName = string.IsNullOrEmpty(directory.RelativePath)
            ? directory.EntryRoot
            : directory.EntryRoot + "/" + directory.RelativePath;
        return GetSafeExtractionPath(extractionRoot, entryName);
    }

    internal static string GetSafeExtractionPath(string extractionRoot, string entryName)
    {
        if (Path.IsPathFullyQualified(entryName))
            throw new InvalidDataException("Absolute archive entries are refused.");
        var root = GetFullPathOrInvalidData(
            extractionRoot + Path.DirectorySeparatorChar,
            "Backup extraction root is invalid.");
        var candidate = GetFullPathOrInvalidData(
            Path.Combine(root, entryName.Replace('/', Path.DirectorySeparatorChar)),
            "Backup archive entry path is invalid.");
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!candidate.StartsWith(root, comparison))
            throw new InvalidDataException("Archive entry escapes the extraction directory.");
        return candidate;
    }

    internal static string GetFullPathOrInvalidData(string path, string message)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidDataException(message, ex);
        }
    }

    internal static bool IsWithin(string candidate, string parent, StringComparison comparison)
    {
        var parentPrefix = Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(parentPrefix, comparison);
    }

    internal static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
