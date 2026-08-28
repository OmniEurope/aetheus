// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Executors;

internal static class OwnerOnlyDirectory
{
    private const UnixFileMode OwnerOnlyMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    internal static void TrySet(
        string directory,
        Action<Exception, string> reportFailure,
        Action<string, UnixFileMode>? setMode = null)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            (setMode ?? File.SetUnixFileMode)(directory, OwnerOnlyMode);
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or PlatformNotSupportedException)
        {
            reportFailure(ex, directory);
        }
    }
}
