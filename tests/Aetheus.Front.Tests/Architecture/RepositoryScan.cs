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

    /// <summary>
    /// The folders holding the maintained pages, their dialogs and the shared components: since
    /// PLAN-009 lot 2, <c>Components/{Module}</c> and <c>Components/Shared</c>, which replaced the
    /// former <c>Pages/</c>, <c>Shared/</c>, <c>Services/</c> and <c>Helpers/</c> folders.
    /// </summary>
    internal static IReadOnlyList<string> PageRoots =>
    [
        Path.Combine(Root, "src", "Aetheus.Front", "Components")
    ];

    /// <summary>
    /// Enumerates several folders as one scan and fails loudly when the union is empty (a single
    /// folder may be absent or empty while the others carry the files). Each file comes with the
    /// folder it was found under, for relative paths.
    /// </summary>
    internal static IReadOnlyList<(string Root, string File)> EnumerateUnion(
        IEnumerable<string> directories,
        string pattern)
    {
        var roots = directories.ToList();
        var files = roots
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories)
                .Select(file => (Root: directory, File: file)))
            .ToList();
        if (files.Count == 0)
            throw new InvalidOperationException(
                $"Scanning '{string.Join("', '", roots)}' for '{pattern}' returned no file. A guard that scans "
                + "nothing passes vacuously, so this is reported as a failure rather than a green run.");
        return files;
    }

}
