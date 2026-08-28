// SPDX-License-Identifier: EUPL-1.2
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using static Aetheus.Agent.Core.Operations.BackupArchivePathHelpers;

namespace Aetheus.Agent.Core.Operations;

internal static class BackupArchiveBundle
{
    private const int CurrentFormatVersion = 2;
    private const string ManifestEntryName = "manifest.json";
    private const long MaxManifestBytes = 8 * 1024 * 1024;
    internal const long MaxCompressedBundleBytes = 128L * 1024 * 1024 * 1024;
    internal const long MaxExpandedBundleBytes = 512L * 1024 * 1024 * 1024;

    internal static IReadOnlyList<string> ParseFilePaths(IReadOnlyDictionary<string, string> environment)
    {
        if (!environment.TryGetValue(BackupConstants.FilePathsEnvVar, out var json)
            || string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Backup file paths are not valid JSON.", ex);
        }
    }

    internal static async Task CreateAsync(
        string archivePath,
        string? databaseDumpPath,
        IReadOnlyList<string> requestedPaths,
        CancellationToken ct)
    {
        var sources = ValidateSources(requestedPaths, archivePath);
        if (databaseDumpPath is null && sources.Count == 0)
            throw new InvalidDataException("A backup requires a database or at least one file path.");
        if (databaseDumpPath is not null && !File.Exists(databaseDumpPath))
            throw new FileNotFoundException("The database dump was not created.", databaseDumpPath);

        var archiveDirectory = Path.GetDirectoryName(archivePath)
            ?? throw new InvalidDataException("Backup archive path has no parent directory.");
        BackupFilesystemMetadata.CreatePrivateDirectory(archiveDirectory);
        var temporaryArchive = archivePath + $".{Guid.NewGuid():N}.partial";
        var manifest = new BackupBundleManifest { Version = CurrentFormatVersion };

        try
        {
            var archiveOptions = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.ReadWrite,
                Share = FileShare.None,
                BufferSize = 81920,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            };
            if (!OperatingSystem.IsWindows()) archiveOptions.UnixCreateMode = BackupFilesystemMetadata.PrivateFileMode;
            await using (var output = new FileStream(temporaryArchive, archiveOptions))
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false))
            {
                if (databaseDumpPath is not null)
                {
                    const string databaseEntry = "database/dump";
                    var metadata = await AddFileAsync(zip, databaseEntry, databaseDumpPath, ct)
                        .ConfigureAwait(false);
                    manifest.Database = metadata;
                }

                for (var index = 0; index < sources.Count; index++)
                {
                    var source = sources[index];
                    var sourceManifest = new BackupBundleSource
                    {
                        OriginalPath = source,
                        IsDirectory = Directory.Exists(source),
                        EntryRoot = $"files/{index}"
                    };
                    manifest.Sources.Add(sourceManifest);

                    if (sourceManifest.IsDirectory)
                    {
                        foreach (var directory in EnumerateDirectoriesWithoutLinks(source))
                        {
                            var relative = Path.GetRelativePath(source, directory).Replace('\\', '/');
                            manifest.Directories.Add(CreateDirectoryMetadata(
                                sourceManifest.EntryRoot,
                                relative == "." ? string.Empty : relative,
                                directory));
                        }
                        foreach (var file in EnumerateFilesWithoutLinks(source))
                        {
                            var relative = Path.GetRelativePath(source, file).Replace('\\', '/');
                            var entryName = $"{sourceManifest.EntryRoot}/{relative}";
                            manifest.Files.Add(await AddFileAsync(zip, entryName, file, ct)
                                .ConfigureAwait(false));
                        }
                    }
                    else
                    {
                        var entryName = $"{sourceManifest.EntryRoot}/{Path.GetFileName(source)}";
                        manifest.Files.Add(await AddFileAsync(zip, entryName, source, ct)
                            .ConfigureAwait(false));
                    }
                }

                var manifestEntry = zip.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
                await using var manifestStream = manifestEntry.Open();
                await JsonSerializer.SerializeAsync(manifestStream, manifest, cancellationToken: ct)
                    .ConfigureAwait(false);
            }

            File.Move(temporaryArchive, archivePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryArchive)) File.Delete(temporaryArchive);
        }
    }

    internal static async Task<VerifiedBackupBundle> OpenVerifiedAsync(
        string archivePath,
        CancellationToken ct)
    {
        var extractionRoot = Path.Combine(Path.GetTempPath(), $"aetheus-backup-{Guid.NewGuid():N}");
        BackupFilesystemMetadata.CreatePrivateDirectory(extractionRoot);
        try
        {
            using var zip = ZipFile.OpenRead(archivePath);
            ValidateExtractionBudget(zip.Entries.Select(entry => (entry.CompressedLength, entry.Length)));
            var manifestEntry = zip.GetEntry(ManifestEntryName)
                ?? throw new InvalidDataException("Backup manifest is missing.");
            if (manifestEntry.Length > MaxManifestBytes)
                throw new InvalidDataException("Backup manifest exceeds the supported size.");

            BackupBundleManifest manifest;
            await using (var stream = manifestEntry.Open())
            {
                manifest = await JsonSerializer.DeserializeAsync<BackupBundleManifest>(stream, cancellationToken: ct)
                    .ConfigureAwait(false)
                    ?? throw new InvalidDataException("Backup manifest is empty.");
            }

            ValidateManifest(manifest);
            var expectedEntries = manifest.Files.Select(file => file.EntryName)
                .Append(ManifestEntryName)
                .ToHashSet(StringComparer.Ordinal);
            if (manifest.Database is not null) expectedEntries.Add(manifest.Database.EntryName);
            if (zip.Entries.Count != expectedEntries.Count
                || zip.Entries.Any(entry => !expectedEntries.Contains(entry.FullName)))
            {
                throw new InvalidDataException("Backup contains unlisted or duplicate entries.");
            }

            string? databaseDumpPath = null;
            if (manifest.Database is not null)
            {
                databaseDumpPath = await ExtractVerifiedFileAsync(
                    zip, manifest.Database, extractionRoot, ct).ConfigureAwait(false);
            }

            foreach (var source in manifest.Sources.Where(source => source.IsDirectory))
                BackupFilesystemMetadata.CreatePrivateDirectory(GetSafeExtractionPath(extractionRoot, source.EntryRoot));
            foreach (var directory in manifest.Directories.OrderBy(item => item.RelativePath.Count(character => character == '/')))
                BackupFilesystemMetadata.CreatePrivateDirectory(GetDirectoryExtractionPath(extractionRoot, directory));
            foreach (var file in manifest.Files)
                await ExtractVerifiedFileAsync(zip, file, extractionRoot, ct).ConfigureAwait(false);
            foreach (var directory in manifest.Directories.OrderByDescending(item => item.RelativePath.Count(character => character == '/')))
                BackupFilesystemMetadata.ApplyDirectoryMetadata(
                    GetDirectoryExtractionPath(extractionRoot, directory), directory);

            return new VerifiedBackupBundle(extractionRoot, databaseDumpPath, manifest.Sources);
        }
        catch
        {
            TryDeleteDirectory(extractionRoot);
            throw;
        }
    }

    internal static async Task RestoreFilesAsync(
        VerifiedBackupBundle bundle,
        CancellationToken ct,
        Action<BackupFileRestoreBoundary, int>? boundaryHook = null)
        => await BackupBundleRestorer.RestoreFilesAsync(bundle, GetSafeExtractionPath, ct, boundaryHook)
            .ConfigureAwait(false);

    private static List<string> ValidateSources(IReadOnlyList<string> requestedPaths, string archivePath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var sources = new List<string>(requestedPaths.Count);
        foreach (var requested in requestedPaths)
        {
            var fullPath = ValidateSource(requested, comparison);
            if (sources.Any(existing => existing.Equals(fullPath, comparison))) continue;
            sources.Add(fullPath);
        }

        ValidateSourcesDoNotOverlap(sources, comparison);
        ValidateArchiveLocation(sources, archivePath, comparison);
        return sources;
    }

    private static string ValidateSource(string requested, StringComparison comparison)
    {
        if (string.IsNullOrWhiteSpace(requested) || !Path.IsPathFullyQualified(requested))
            throw new InvalidDataException($"Backup path '{requested}' must be absolute.");
        var fullPath = Path.TrimEndingDirectorySeparator(GetFullPathOrInvalidData(
            requested, "Backup source path is invalid."));
        var pathRoot = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(pathRoot))
            throw new InvalidDataException("Backup source path has no filesystem root.");
        var root = Path.TrimEndingDirectorySeparator(pathRoot);
        if (fullPath.Equals(root, comparison))
            throw new InvalidDataException("Backing up a filesystem root is refused.");
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            throw new FileNotFoundException("Configured backup path does not exist.", fullPath);
        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Symbolic links are not supported as backup roots: '{fullPath}'.");
        return fullPath;
    }

    private static void ValidateSourcesDoNotOverlap(IReadOnlyList<string> sources, StringComparison comparison)
    {
        for (var left = 0; left < sources.Count; left++)
        {
            for (var right = left + 1; right < sources.Count; right++)
            {
                if (IsWithin(sources[left], sources[right], comparison)
                    || IsWithin(sources[right], sources[left], comparison))
                {
                    throw new InvalidDataException("Configured backup paths must not overlap.");
                }
            }
        }
    }

    private static void ValidateArchiveLocation(
        IEnumerable<string> sources,
        string archivePath,
        StringComparison comparison)
    {
        var fullArchivePath = GetFullPathOrInvalidData(archivePath, "Backup archive path is invalid.");
        foreach (var directory in sources.Where(Directory.Exists))
        {
            if (IsWithin(fullArchivePath, directory, comparison))
                throw new InvalidDataException("The backup archive cannot be written inside a backed-up directory.");
        }
    }

    private static IEnumerable<string> EnumerateFilesWithoutLinks(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(directory).Order(StringComparer.Ordinal))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"Symbolic links are not supported in backups: '{file}'.");
                yield return file;
            }
            foreach (var child in Directory.EnumerateDirectories(directory).OrderDescending())
            {
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"Symbolic links are not supported in backups: '{child}'.");
                pending.Push(child);
            }
        }
    }

    private static IEnumerable<string> EnumerateDirectoriesWithoutLinks(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Symbolic links are not supported in backups: '{directory}'.");
            yield return directory;
            foreach (var child in Directory.EnumerateDirectories(directory).OrderDescending())
                pending.Push(child);
        }
    }

    private static BackupBundleDirectory CreateDirectoryMetadata(
        string entryRoot,
        string relativePath,
        string sourcePath) => new()
    {
        EntryRoot = entryRoot,
        RelativePath = relativePath,
        LastWriteTimeUtcTicks = Directory.GetLastWriteTimeUtc(sourcePath).Ticks,
        UnixMode = BackupFilesystemMetadata.GetUnixMode(sourcePath)
    };

    private static async Task<BackupBundleFile> AddFileAsync(
        ZipArchive zip,
        string entryName,
        string sourcePath,
        CancellationToken ct)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var source = new FileStream(
            sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = entry.Open();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long length = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            hash.AppendData(buffer, 0, read);
            length += read;
        }
        return new BackupBundleFile
        {
            EntryName = entryName,
            Length = length,
            Sha256 = Convert.ToHexString(hash.GetHashAndReset()),
            LastWriteTimeUtcTicks = File.GetLastWriteTimeUtc(sourcePath).Ticks,
            UnixMode = BackupFilesystemMetadata.GetUnixMode(sourcePath)
        };
    }

    private static async Task<string> ExtractVerifiedFileAsync(
        ZipArchive zip,
        BackupBundleFile expected,
        string extractionRoot,
        CancellationToken ct)
    {
        var entry = zip.GetEntry(expected.EntryName)
            ?? throw new InvalidDataException($"Backup entry '{expected.EntryName}' is missing.");
        if (entry.Length != expected.Length)
            throw new InvalidDataException($"Backup entry '{expected.EntryName}' has an unexpected expanded size.");
        var destinationPath = GetSafeExtractionPath(extractionRoot, expected.EntryName);
        BackupFilesystemMetadata.CreatePrivateDirectory(Path.GetDirectoryName(destinationPath)!);
        await using var input = entry.Open();
        var outputOptions = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = 81920,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        };
        if (!OperatingSystem.IsWindows()) outputOptions.UnixCreateMode = BackupFilesystemMetadata.PrivateFileMode;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long length = 0;
        await using (var output = new FileStream(destinationPath, outputOptions))
        {
            int read;
            while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                hash.AppendData(buffer, 0, read);
                length = checked(length + read);
                if (length > expected.Length)
                    throw new InvalidDataException($"Backup entry '{expected.EntryName}' exceeds its declared size.");
            }
        }
        var actualHash = Convert.ToHexString(hash.GetHashAndReset());
        if (length != expected.Length || !actualHash.Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Backup entry '{expected.EntryName}' failed integrity verification.");
        BackupFilesystemMetadata.ApplyFileMetadata(destinationPath, expected);
        return destinationPath;
    }

    internal static void ValidateExtractionBudget(IEnumerable<(long CompressedBytes, long ExpandedBytes)> entries)
    {
        long compressed = 0;
        long expanded = 0;
        try
        {
            foreach (var (compressedBytes, expandedBytes) in entries)
            {
                if (compressedBytes < 0 || expandedBytes < 0)
                    throw new InvalidDataException("Backup contains an entry with an invalid size.");
                compressed = checked(compressed + compressedBytes);
                expanded = checked(expanded + expandedBytes);
                if (compressed > MaxCompressedBundleBytes || expanded > MaxExpandedBundleBytes)
                    throw new InvalidDataException("Backup exceeds the supported compressed or expanded size budget.");
            }
        }
        catch (OverflowException ex)
        {
            throw new InvalidDataException("Backup size metadata overflowed the supported budget.", ex);
        }
    }

    private static void ValidateManifest(BackupBundleManifest manifest)
    {
        if (manifest.Version is < 1 or > CurrentFormatVersion)
            throw new InvalidDataException($"Unsupported backup format version {manifest.Version}.");
        if (manifest.Sources.Count > 200 || manifest.Files.Count > 100_000 || manifest.Directories.Count > 100_000)
            throw new InvalidDataException("Backup manifest exceeds supported collection limits.");
        if (manifest.Database is null && manifest.Sources.Count == 0)
            throw new InvalidDataException("Backup manifest contains no database or file sources.");

        ValidateExtractionBudget(manifest.Files.Select(file => (0L, file.Length))
            .Concat(manifest.Database is null ? [] : [(0L, manifest.Database.Length)]));

        var entryNames = new HashSet<string>(StringComparer.Ordinal);
        if (manifest.Database is not null) ValidateFile(manifest.Database, entryNames, manifest.Version);
        foreach (var file in manifest.Files) ValidateFile(file, entryNames, manifest.Version);

        var sourceRoots = ValidateManifestSources(manifest);
        ValidateDirectories(manifest, sourceRoots);
    }

    private static ISet<string> ValidateManifestSources(BackupBundleManifest manifest)
    {
        var entryRoots = new HashSet<string>(StringComparer.Ordinal);
        var originalPaths = new List<string>(manifest.Sources.Count);
        var sourceFileCounts = IndexSourceFileCounts(manifest.Files.Select(file => file.EntryName));
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        foreach (var source in manifest.Sources)
        {
            var originalPath = ValidateManifestSource(
                source,
                sourceFileCounts.GetValueOrDefault(source.EntryRoot),
                entryRoots,
                originalPaths,
                pathComparison);
            originalPaths.Add(originalPath);
            _ = GetSafeExtractionPath(Path.GetTempPath(), source.EntryRoot);
        }

        if (sourceFileCounts.Keys.Any(root => !entryRoots.Contains(root)))
            throw new InvalidDataException("Backup file entry is not associated with a declared source.");
        return entryRoots;
    }

    private static string ValidateManifestSource(
        BackupBundleSource source,
        int sourceFileCount,
        ISet<string> entryRoots,
        IReadOnlyList<string> originalPaths,
        StringComparison pathComparison)
    {
        if (string.IsNullOrWhiteSpace(source.OriginalPath)
            || !Path.IsPathFullyQualified(source.OriginalPath)
            || string.IsNullOrWhiteSpace(source.EntryRoot)
            || !entryRoots.Add(source.EntryRoot)
            || !source.EntryRoot.StartsWith("files/", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Backup source metadata is invalid.");
        }

        var originalPath = Path.TrimEndingDirectorySeparator(GetFullPathOrInvalidData(
            source.OriginalPath, "Backup source target path is invalid."));
        var pathRoot = Path.GetPathRoot(originalPath);
        if (string.IsNullOrEmpty(pathRoot))
            throw new InvalidDataException("Backup source target has no filesystem root.");
        var filesystemRoot = Path.TrimEndingDirectorySeparator(pathRoot);
        if (originalPath.Equals(filesystemRoot, pathComparison)
            || originalPaths.Any(existing => existing.Equals(originalPath, pathComparison)
                || IsWithin(existing, originalPath, pathComparison)
                || IsWithin(originalPath, existing, pathComparison)))
        {
            throw new InvalidDataException("Backup source targets are unsafe or overlap.");
        }

        if (!source.IsDirectory && sourceFileCount != 1)
            throw new InvalidDataException("A file backup source must contain exactly one file entry.");
        return originalPath;
    }

    internal static IReadOnlyDictionary<string, int> IndexSourceFileCounts(IEnumerable<string> entryNames)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entryName in entryNames)
        {
            if (!TryGetSourceRoot(entryName, out var sourceRoot))
                throw new InvalidDataException("Backup file entry is not associated with a declared source.");
            counts[sourceRoot] = counts.GetValueOrDefault(sourceRoot) + 1;
        }
        return counts;
    }

    private static void ValidateFile(BackupBundleFile file, ISet<string> entryNames, int manifestVersion)
    {
        if (string.IsNullOrWhiteSpace(file.EntryName)
            || !entryNames.Add(file.EntryName)
            || file.Length < 0
            || file.Sha256.Length != 64
            || file.Sha256.Any(character => !Uri.IsHexDigit(character))
            || manifestVersion >= 2 && !HasValidMetadata(file.LastWriteTimeUtcTicks, file.UnixMode))
        {
            throw new InvalidDataException("Backup file metadata is invalid.");
        }
        _ = GetSafeExtractionPath(Path.GetTempPath(), file.EntryName);
    }

    private static void ValidateDirectories(BackupBundleManifest manifest, ISet<string> sourceRoots)
    {
        if (manifest.Version < 2)
        {
            if (manifest.Directories.Count != 0)
                throw new InvalidDataException("Legacy backup manifests cannot contain directory metadata.");
            return;
        }

        var directoryKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var directory in manifest.Directories)
        {
            var key = directory.EntryRoot + "/" + directory.RelativePath;
            if (!sourceRoots.Contains(directory.EntryRoot)
                || Path.IsPathFullyQualified(directory.RelativePath)
                || directory.RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .Any(segment => segment is "." or "..")
                || !directoryKeys.Add(key)
                || !HasValidMetadata(directory.LastWriteTimeUtcTicks, directory.UnixMode))
            {
                throw new InvalidDataException("Backup directory metadata is invalid.");
            }
            _ = GetDirectoryExtractionPath(Path.GetTempPath(), directory);
        }

        foreach (var source in manifest.Sources.Where(source => source.IsDirectory))
        {
            if (!directoryKeys.Contains(source.EntryRoot + "/"))
                throw new InvalidDataException("Backup directory source is missing its root metadata.");
        }
    }

}
