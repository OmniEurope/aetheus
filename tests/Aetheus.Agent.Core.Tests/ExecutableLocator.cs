// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Tests;

/// <summary>
/// Resolves an external tool: the caller's well-known locations first, then PATH.
///
/// Why it exists: these suites probed one absolute path (<c>C:\Program Files\Git\usr\bin\sh.exe</c>)
/// and skipped themselves when it did not match, so a tool installed anywhere else silently disabled
/// the tests instead of running them.
///
/// Why well-known paths win over PATH: on Windows, PATH resolves the POSIX shell names to the WSL
/// shim in System32, which cannot execute a script addressed by a Windows path. The caller names the
/// interpreter it actually means; PATH remains the fallback for hosts that install it elsewhere.
///
/// <see cref="Require"/> throws rather than skipping: the scripts under test are shipped and run by
/// the agent, so a host that cannot execute them must say so out loud.
/// </summary>
internal static class ExecutableLocator
{
    internal static string Require(string name, params string[] wellKnownPaths)
        => TryFind(name, out var path, wellKnownPaths)
            ? path
            : throw new InvalidOperationException(
                $"'{name}' was not found on PATH nor at any well-known location. "
                + "It is a prerequisite of the shipped agent scripts, so this environment cannot run "
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
