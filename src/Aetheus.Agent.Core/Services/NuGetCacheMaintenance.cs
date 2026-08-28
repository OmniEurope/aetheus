// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Services;

/// <summary>
/// Inventories and removes only expired, non-current NuGet package versions beneath a verified
/// packages root. Reparse points and directories without NuGet metadata are never mutated.
/// </summary>
internal sealed class NuGetCacheMaintenance(
    DockerStorageMaintenanceOptions options,
    TimeProvider timeProvider,
    ILogger logger)
{
    internal string PackagesPath
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(options.NuGetPackagesPath))
                return options.NuGetPackagesPath;
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Environment.GetEnvironmentVariable("NUGET_PACKAGES")
                ?? Path.Combine(userProfile, ".nuget", "packages");
        }
    }

    internal Task RunAsync(bool isDryRun, CancellationToken ct)
    {
        if (options.NuGetCacheRetentionDays <= 0)
            return Task.CompletedTask;

        var path = PackagesPath;
        if (!TryResolveSafeRoot(path, out var root))
        {
            logger.LogError("NuGet cache retention refused: unsafe or unavailable packages root {Path}", path);
            return Task.CompletedTask;
        }

        var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddDays(-options.NuGetCacheRetentionDays);
        var candidates = new List<(DirectoryInfo Directory, long Bytes)>();
        try
        {
            foreach (var package in root.EnumerateDirectories())
            {
                ct.ThrowIfCancellationRequested();
                if (IsReparsePoint(package)) continue;

                var versions = package.EnumerateDirectories()
                    .Where(version => IsSafeVersionDirectory(version, root.FullName))
                    .OrderByDescending(version => version.LastWriteTimeUtc)
                    .ToList();
                foreach (var version in versions.Skip(1).Where(version => version.LastWriteTimeUtc < cutoff))
                    candidates.Add((version, MeasureDirectoryBytes(version, ct)));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not inventory NuGet cache {Path}", path);
            return Task.CompletedTask;
        }

        var candidateBytes = candidates.Sum(candidate => candidate.Bytes);
        logger.LogInformation(
            "NuGet cache retention decision: path={Path}, dryRun={DryRun}, retentionDays={RetentionDays}, " +
            "candidateVersions={CandidateVersions}, candidateBytes={CandidateBytes}",
            path, isDryRun, options.NuGetCacheRetentionDays, candidates.Count, FormatBytes(candidateBytes));

        if (isDryRun)
            return Task.CompletedTask;

        var removed = 0;
        long removedBytes = 0;
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!IsSafeVersionDirectory(candidate.Directory, root.FullName))
                {
                    logger.LogWarning(
                        "Skipping NuGet retention candidate that no longer satisfies the package-cache contract: {Path}",
                        candidate.Directory.FullName);
                    continue;
                }
                candidate.Directory.Delete(recursive: true);
                removed++;
                removedBytes += candidate.Bytes;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Could not remove expired NuGet package version {Path}", candidate.Directory.FullName);
            }
        }

        logger.LogInformation(
            "NuGet cache retention result: removedVersions={RemovedVersions}, removedBytes={RemovedBytes}",
            removed, FormatBytes(removedBytes));
        return Task.CompletedTask;
    }

    private static bool IsReparsePoint(FileSystemInfo entry) =>
        (entry.Attributes & FileAttributes.ReparsePoint) != 0;

    private static bool TryResolveSafeRoot(string path, out DirectoryInfo root)
    {
        root = null!;
        try
        {
            var fullPath = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var volumeRoot = Path.GetPathRoot(fullPath)?
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrEmpty(fullPath)
                || string.Equals(fullPath, volumeRoot, OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal)
                || !Directory.Exists(fullPath))
            {
                return false;
            }

            root = new DirectoryInfo(fullPath);
            root.Refresh();
            return !IsReparsePoint(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static bool IsSafeVersionDirectory(DirectoryInfo version, string rootPath)
    {
        try
        {
            version.Refresh();
            if (!version.Exists || IsReparsePoint(version) || version.Parent?.Parent is null)
                return false;

            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(
                    Path.GetFullPath(version.Parent.Parent.FullName)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(rootPath)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    comparison))
            {
                return false;
            }

            var metadata = new FileInfo(Path.Combine(version.FullName, ".nupkg.metadata"));
            metadata.Refresh();
            return metadata.Exists && !IsReparsePoint(metadata);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static long MeasureDirectoryBytes(DirectoryInfo directory, CancellationToken ct)
    {
        try
        {
            long total = 0;
            foreach (var file in directory.EnumerateFiles("*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = false,
                AttributesToSkip = FileAttributes.ReparsePoint
            }))
            {
                ct.ThrowIfCancellationRequested();
                total = checked(total + file.Length);
            }
            return total;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OverflowException)
        {
            return 0;
        }
    }

    private static long GiB(int value) => StorageSizeFormatting.GiB(value);

    private static string FormatBytes(long bytes) => StorageSizeFormatting.FormatBytes(bytes);
}
