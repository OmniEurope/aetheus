// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Tests;

/// <summary>
/// File enumeration for the repository-wide guards of this suite, with the one property the raw
/// <see cref="Directory.EnumerateFiles(string, string, SearchOption)"/> does not have: it refuses to
/// return nothing.
///
/// Why: a guard that scans the repository and finds no file passes. Every assertion it makes is
/// vacuously true over an empty set, so it reports green while protecting nothing. The backend suite
/// hit exactly that (a path filter excluded the whole repository when run from a worktree), and the
/// back and front suites were given this floor on 2026-08-20. This suite was missed in that pass and
/// its four repository-scanning guards were still scanning raw, which the 360 audit caught.
/// </summary>
internal static class RepositoryScan
{
    /// <summary>
    /// The repository root, resolved once. Before the 2026-08-21 remediation (A360-71) every guard
    /// carried its own private copy of this walk - 78 files across the three suites, in 16 slightly
    /// different shapes - so a fix to one never reached the others.
    /// </summary>
    internal static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aetheus.slnx")))
                directory = directory.Parent;
            return directory?.FullName
                   ?? throw new InvalidOperationException("Could not locate repository root (Aetheus.slnx).");
        }
    }


    /// <summary>Enumerates and fails loudly when the scan is empty.</summary>
    internal static IReadOnlyList<string> Enumerate(
        string directory,
        string pattern,
        SearchOption option = SearchOption.AllDirectories)
    {
        var files = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, pattern, option).ToList()
            : [];
        if (files.Count == 0)
            throw new InvalidOperationException(
                $"Scanning '{directory}' for '{pattern}' returned no file. A guard that scans nothing "
                + "passes vacuously, so this is reported as a failure rather than a green run.");
        return files;
    }
}
