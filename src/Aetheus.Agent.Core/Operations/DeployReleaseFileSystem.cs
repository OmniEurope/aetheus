// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Operations;

internal static class DeployReleaseFileSystem
{
    internal const UnixFileMode ReleaseDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute;

    internal static string? ReadLinkTarget(string linkPath)
    {
        try
        {
            var info = new DirectoryInfo(linkPath);
            return info.LinkTarget is null
                ? null
                : Path.GetFullPath(info.LinkTarget, Path.GetDirectoryName(linkPath)!);
        }
        catch (IOException) { return null; }
    }

    internal static void HardenTree(string releaseDir)
    {
        if (!OperatingSystem.IsLinux()) return;
        File.SetUnixFileMode(releaseDir, ReleaseDirectoryMode);
        foreach (var directory in Directory.EnumerateDirectories(releaseDir, "*", SearchOption.AllDirectories))
            File.SetUnixFileMode(directory, ReleaseDirectoryMode);
        foreach (var file in Directory.EnumerateFiles(releaseDir, "*", SearchOption.AllDirectories))
            File.SetUnixFileMode(file, HardenedFileMode(File.GetUnixFileMode(file)));
    }

    internal static UnixFileMode HardenedFileMode(UnixFileMode originalMode)
    {
        var result = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;
        const UnixFileMode anyExecute =
            UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        if ((originalMode & anyExecute) != 0)
            result |= UnixFileMode.UserExecute | UnixFileMode.GroupExecute;
        return result;
    }

    internal static void TryMakeExecutable(string path, ILogger logger)
    {
        if (!OperatingSystem.IsLinux()) return;
        try
        {
            var mode = File.GetUnixFileMode(path);
            File.SetUnixFileMode(path, HardenedFileMode(mode | UnixFileMode.UserExecute));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            logger.LogWarning(ex, "Could not chmod +x the run launcher");
        }
    }
}
