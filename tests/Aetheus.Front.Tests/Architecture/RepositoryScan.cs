// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Tests.Architecture;

/// <summary>
/// File enumeration for repository-wide guards, with one property the raw
/// <see cref="Directory.EnumerateFiles(string, string, SearchOption)"/> does not have: it refuses to
/// return nothing.
///
/// Why: a guard that scans the repository and finds no file passes. Every assertion it makes is
/// vacuously true over an empty set, so it reports green while protecting nothing, and nobody
/// notices. The back suite hit exactly that (DeliveryReproducibilityAuditTests excluded the whole
/// repository when run from a worktree); its twin of this helper closed it there, this one closes
/// it for the front guards.
/// </summary>
internal static class RepositoryScan
{
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
