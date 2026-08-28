// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Operations;

namespace Aetheus.Agent.Core.Tests;

public sealed class WorkspaceFileMatcherTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"aetheus-matcher-{Guid.NewGuid():N}");
    private readonly string outside = Path.Combine(Path.GetTempPath(), $"aetheus-matcher-outside-{Guid.NewGuid():N}");

    public WorkspaceFileMatcherTests()
    {
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
    }

    [Fact]
    public void RecursiveGlob_SkipsDirectorySymlinkOutsideWorkspace()
    {
        File.WriteAllText(Path.Combine(root, "inside.txt"), "inside");
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "outside");
        LinkOutsideWorkspace(Path.Combine(root, "escape"), outside);

        var matches = WorkspaceFileMatcher.GetMatchingFiles(root, "**/*.txt").ToList();

        Assert.Single(matches);
        Assert.EndsWith("inside.txt", matches[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// Creates a directory reparse point pointing outside the workspace. On Windows a symbolic link
    /// needs the developer-mode or SeCreateSymbolicLink privilege, which is exactly why this test used
    /// to skip itself; a junction is the same reparse point for the matcher's purposes and needs no
    /// privilege. Elsewhere the symbolic link is used directly.
    /// </summary>
    private static void LinkOutsideWorkspace(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }

        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            using var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                ArgumentList = { "/c", "mklink", "/J", link, target },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }) ?? throw new InvalidOperationException("Could not start cmd.exe to create a junction.", ex);
            var error = mklink.StandardError.ReadToEnd();
            mklink.WaitForExit(10000);
            if (mklink.ExitCode != 0 || !Directory.Exists(link))
                throw new InvalidOperationException($"Could not create a junction at '{link}': {error}", ex);
        }
    }

    public void Dispose()
    {
        // Unlink the reparse point before the recursive delete: a junction is a real directory entry,
        // so recursing into it would walk (and delete) the target that lives outside the workspace.
        var escape = Path.Combine(root, "escape");
        if (Directory.Exists(escape)) Directory.Delete(escape);
        Directory.Delete(root, recursive: true);
        Directory.Delete(outside, recursive: true);
    }
}
