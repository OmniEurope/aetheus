// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Operations;

internal enum BackupFileRestoreBoundary
{
    SourceStaged,
    PreviousMoved,
    SourceSwapped
}

internal static class BackupBundleRestorer
{
    internal static async Task RestoreFilesAsync(
        VerifiedBackupBundle bundle,
        Func<string, string, string> getSafeExtractionPath,
        CancellationToken ct,
        Action<BackupFileRestoreBoundary, int>? boundaryHook = null)
    {
        var entries = new List<RestoreEntry>(bundle.Sources.Count);
        try
        {
            for (var index = 0; index < bundle.Sources.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                var source = bundle.Sources[index];
                var extractedRoot = getSafeExtractionPath(bundle.ExtractionRoot, source.EntryRoot);
                var entry = CreateEntry(source, extractedRoot);
                entries.Add(entry);
                await StageAsync(entry, ct).ConfigureAwait(false);
                boundaryHook?.Invoke(BackupFileRestoreBoundary.SourceStaged, index);
            }

            for (var index = 0; index < entries.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                var entry = entries[index];
                if (EntryExists(entry.Target, entry.IsDirectory))
                {
                    Move(entry.Target, entry.Previous, entry.IsDirectory);
                    entry.PreviousMoved = true;
                    boundaryHook?.Invoke(BackupFileRestoreBoundary.PreviousMoved, index);
                }
                Move(entry.Staged, entry.Target, entry.IsDirectory);
                entry.SourceSwapped = true;
                boundaryHook?.Invoke(BackupFileRestoreBoundary.SourceSwapped, index);
            }

            foreach (var entry in entries)
                TryDeleteIfExists(entry.Previous, entry.IsDirectory);
        }
        catch
        {
            for (var index = entries.Count - 1; index >= 0; index--)
            {
                var entry = entries[index];
                if (entry.SourceSwapped) DeleteIfExists(entry.Target, entry.IsDirectory);
                if (entry.PreviousMoved && EntryExists(entry.Previous, entry.IsDirectory))
                    Move(entry.Previous, entry.Target, entry.IsDirectory);
                DeleteIfExists(entry.Staged, entry.IsDirectory);
            }
            throw;
        }
    }

    private static RestoreEntry CreateEntry(BackupBundleSource source, string extractedRoot)
    {
        var target = source.OriginalPath;
        var parent = Path.GetDirectoryName(target)
            ?? throw new InvalidDataException("Backup target has no parent directory.");
        BackupFilesystemMetadata.CreatePrivateDirectory(parent);
        var suffix = Guid.NewGuid().ToString("N");
        var extracted = source.IsDirectory
            ? extractedRoot
            : Directory.EnumerateFiles(extractedRoot, "*", SearchOption.TopDirectoryOnly).Single();
        return new RestoreEntry(
            extracted,
            target,
            Path.Combine(parent, $".aetheus-restore-{suffix}"),
            Path.Combine(parent, $".aetheus-previous-{suffix}"),
            source.IsDirectory);
    }

    private static async Task StageAsync(RestoreEntry entry, CancellationToken ct)
    {
        if (entry.IsDirectory)
        {
            await CopyDirectoryAsync(entry.Extracted, entry.Staged, ct).ConfigureAwait(false);
            return;
        }

        await using (var input = File.OpenRead(entry.Extracted))
        await using (var output = new FileStream(entry.Staged, BackupFilesystemMetadata.CreatePrivateFileOptions()))
            await input.CopyToAsync(output, ct).ConfigureAwait(false);
        BackupFilesystemMetadata.Copy(entry.Extracted, entry.Staged);
    }

    private static bool EntryExists(string path, bool directory)
        => directory ? Directory.Exists(path) : File.Exists(path);

    private static void Move(string source, string target, bool directory)
    {
        if (directory) Directory.Move(source, target);
        else File.Move(source, target);
    }

    private static void DeleteIfExists(string path, bool directory)
    {
        if (directory)
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void TryDeleteIfExists(string path, bool directory)
    {
        try { DeleteIfExists(path, directory); }
        catch (IOException) { } // best-effort cleanup: a leftover directory must not replace the outcome being reported
        catch (UnauthorizedAccessException) { } // same: best-effort cleanup
    }

    private static async Task CopyDirectoryAsync(string source, string target, CancellationToken ct)
    {
        BackupFilesystemMetadata.CreatePrivateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            BackupFilesystemMetadata.CreatePrivateDirectory(
                Path.Combine(target, Path.GetRelativePath(source, directory)));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            BackupFilesystemMetadata.CreatePrivateDirectory(Path.GetDirectoryName(destination)!);
            await using var input = File.OpenRead(file);
            await using (var output = new FileStream(destination, BackupFilesystemMetadata.CreatePrivateFileOptions()))
                await input.CopyToAsync(output, ct).ConfigureAwait(false);
            BackupFilesystemMetadata.Copy(file, destination);
        }

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Count(character => character == Path.DirectorySeparatorChar)))
        {
            BackupFilesystemMetadata.Copy(
                directory,
                Path.Combine(target, Path.GetRelativePath(source, directory)));
        }
        BackupFilesystemMetadata.Copy(source, target);
    }

    private sealed class RestoreEntry(
        string extracted,
        string target,
        string staged,
        string previous,
        bool isDirectory)
    {
        internal string Extracted { get; } = extracted;
        internal string Target { get; } = target;
        internal string Staged { get; } = staged;
        internal string Previous { get; } = previous;
        internal bool IsDirectory { get; } = isDirectory;
        internal bool PreviousMoved { get; set; }
        internal bool SourceSwapped { get; set; }
    }
}
