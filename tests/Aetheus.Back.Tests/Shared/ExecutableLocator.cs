// SPDX-License-Identifier: EUPL-1.2
// Flat namespace like every other file under Shared/: a nested Aetheus.Back.Tests.Shared would
// shadow Aetheus.Shared for every test that qualifies it as Shared.Enums.*.
namespace Aetheus.Back.Tests;

/// <summary>
/// Resolves an external tool: the caller's well-known locations first, then PATH.
///
/// Why it exists: several suites probed one absolute path (<c>C:\Program Files\nodejs\node.exe</c>,
/// <c>C:\Program Files\Git\usr\bin\sh.exe</c>) and skipped themselves when it did not match. A tool
/// installed anywhere else silently disabled the tests instead of running them.
///
/// Why well-known paths win over PATH: on Windows, PATH resolves <c>bash</c> to the WSL shim in
/// System32, which cannot execute a script addressed by a Windows path. Searching PATH first made
/// these tests fail under PowerShell (where System32 comes first) while passing under Git Bash.
/// The caller names the interpreter it actually means; PATH stays as the fallback that fixes the
/// original bug for hosts where the tool lives somewhere else entirely.
///
/// <see cref="Require"/> throws rather than skipping: node, git and a POSIX shell are prerequisites
/// of the delivery pipelines themselves, so a host without them must say so out loud.
/// </summary>
internal static class ExecutableLocator
{
    /// <summary>The resolved absolute path, or a failed assertion naming what is missing.</summary>
    internal static string Require(string name, params string[] wellKnownPaths)
        => TryFind(name, out var path, wellKnownPaths)
            ? path
            : throw new InvalidOperationException(
                $"'{name}' was not found on PATH nor at any well-known location. "
                + "It is a prerequisite of the delivery pipelines, so this environment cannot run "
                + "this suite honestly. Install it instead of disabling the test.");

    internal static bool TryFind(string name, out string path, params string[] wellKnownPaths)
    {
        foreach (var candidate in wellKnownPaths.Concat(SearchPath(name)))
        {
            if (!File.Exists(candidate)) continue;
            path = candidate;
            return true;
        }

        path = string.Empty;
        return false;
    }

    private static IEnumerable<string> SearchPath(string name)
    {
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // On Windows the bare name carries no extension; PATHEXT is what the shell appends.
        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [string.Empty];

        foreach (var directory in directories)
        {
            if (Path.GetFileName(name).Length != name.Length) continue;
            foreach (var extension in extensions)
            {
                string candidate;
                try
                {
                    candidate = Path.Combine(directory, name + extension);
                }
                catch (ArgumentException)
                {
                    // A malformed PATH entry must not take the whole lookup down.
                    continue;
                }
                yield return candidate;
            }
        }
    }
}
