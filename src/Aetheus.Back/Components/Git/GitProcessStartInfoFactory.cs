// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using System.Text;

namespace Aetheus.Back.Components.Git;

internal static class GitProcessStartInfoFactory
{
    public static ProcessStartInfo Create(string workingDirectory, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        NeutralizeInheritedGitEnvironment(startInfo);
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "never";
        return startInfo;
    }

    // WHY: a git child launched from inside a git hook inherits the hooking repository's variables
    // (GIT_DIR, and from a linked worktree GIT_COMMON_DIR too) and silently operates on that repository
    // instead of WorkingDirectory. Removing only four of them let the 2026-09-17 incident through.
    internal static void NeutralizeInheritedGitEnvironment(ProcessStartInfo startInfo) =>
        GitRepositoryEnvironment.Neutralize(startInfo.Environment);
}
