// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Operations;

internal static class BackupFilesystemMetadata
{
    internal const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode PrivateDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    internal static int? GetUnixMode(string path)
        => OperatingSystem.IsWindows() ? null : (int)File.GetUnixFileMode(path);

    internal static void ApplyFileMetadata(string path, BackupBundleFile metadata)
    {
        if (metadata.LastWriteTimeUtcTicks is { } ticks)
            File.SetLastWriteTimeUtc(path, new DateTime(ticks, DateTimeKind.Utc));
        if (!OperatingSystem.IsWindows() && metadata.UnixMode is { } unixMode)
            File.SetUnixFileMode(path, (UnixFileMode)unixMode);
    }

    internal static void ApplyDirectoryMetadata(string path, BackupBundleDirectory metadata)
    {
        if (metadata.LastWriteTimeUtcTicks is { } ticks)
            Directory.SetLastWriteTimeUtc(path, new DateTime(ticks, DateTimeKind.Utc));
        if (!OperatingSystem.IsWindows() && metadata.UnixMode is { } unixMode)
            File.SetUnixFileMode(path, (UnixFileMode)unixMode);
    }

    internal static void Copy(string source, string target)
    {
        if (Directory.Exists(source))
            Directory.SetLastWriteTimeUtc(target, Directory.GetLastWriteTimeUtc(source));
        else
            File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(source));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(target, File.GetUnixFileMode(source));
    }

    // A new owner-only file opened for sequential async I/O; CreateNew refuses to follow or reuse an existing path.
    internal static FileStreamOptions CreatePrivateFileOptions(FileAccess access = FileAccess.Write)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = access,
            Share = FileShare.None,
            BufferSize = 81920,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = PrivateFileMode;
        return options;
    }

    internal static void CreatePrivateDirectory(string path)
    {
        var existed = Directory.Exists(path);
        Directory.CreateDirectory(path);
        if (!existed && !OperatingSystem.IsWindows()) File.SetUnixFileMode(path, PrivateDirectoryMode);
    }
}
