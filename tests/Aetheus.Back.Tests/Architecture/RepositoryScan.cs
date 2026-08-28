// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Tests.Architecture;

/// <summary>
/// File enumeration for repository-wide guards, with one property the raw
/// <see cref="Directory.EnumerateFiles(string, string, SearchOption)"/> does not have: it refuses to
/// return nothing.
///
/// Why: a guard that scans the repository and finds no file passes. Every assertion it makes is
/// vacuously true over an empty set, so it reports green while protecting nothing, and nobody
/// notices. That is not hypothetical - DeliveryReproducibilityAuditTests excluded the whole
/// repository when the suite ran from a worktree, and only the two assertions in that file carrying
/// a count floor caught it.
///
/// <see cref="Enumerate"/> is for a scan that must find something (the repository always contains
/// C# files). <see cref="EnumerateOptional"/> is for one leg of a multi-directory sweep where a
/// single combination may legitimately be empty; the caller then asserts the total with
/// <see cref="AssertScanned"/>.
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
        var files = EnumerateOptional(directory, pattern, option);
        if (files.Count == 0)
            throw new InvalidOperationException(
                $"Scanning '{directory}' for '{pattern}' returned no file. A guard that scans nothing "
                + "passes vacuously, so this is reported as a failure rather than a green run.");
        return files;
    }

    /// <summary>
    /// Enumerates without the floor. Only for one leg of a sweep whose other legs carry the signal;
    /// the caller must still prove the sweep as a whole saw something.
    /// </summary>
    internal static IReadOnlyList<string> EnumerateOptional(
        string directory,
        string pattern,
        SearchOption option = SearchOption.AllDirectories)
        => Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, pattern, option).ToList()
            : [];

    /// <summary>The floor for a sweep assembled from several optional legs.</summary>
    internal static void AssertScanned(int count, int minimum, string what)
        => Assert.True(count >= minimum,
            $"The {what} scan is unexpectedly small ({count} < {minimum}); a guard that scans nothing "
            + "passes vacuously.");
}
