// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Operations;

internal static class WorkspaceFileMatcher
{
    public static IEnumerable<string> GetMatchingFiles(string baseDir, string pattern)
    {
        // Pipeline patterns are user-authored. Never let rooted or parent-relative paths escape the
        // agent workspace when collecting, substituting or publishing files.
        if (Path.IsPathRooted(pattern)
            || pattern.Split('/', '\\').Any(segment => segment == ".."))
            return [];

        var baseFull = Path.GetFullPath(baseDir);
        if (pattern.Contains('*') || pattern.Contains('?'))
        {
            var searchPattern = Path.GetFileName(pattern);
            var searchDir = Path.GetDirectoryName(pattern)?.Replace("**", "").TrimStart(Path.DirectorySeparatorChar, '/');
            var fullSearchDir = string.IsNullOrEmpty(searchDir) ? baseDir : Path.Combine(baseDir, searchDir);
            if (!Directory.Exists(fullSearchDir) || !IsUnderBase(fullSearchDir, baseFull)) return [];

            return EnumerateConfinedFiles(
                fullSearchDir,
                searchPattern,
                recursive: pattern.Contains("**"),
                baseFull);
        }

        var fullPath = Path.Combine(baseDir, pattern);
        if (!IsUnderBase(fullPath, baseFull)) return [];
        if (File.Exists(fullPath)) return [fullPath];
        if (Directory.Exists(fullPath)) return EnumerateConfinedFiles(fullPath, "*", recursive: true, baseFull);
        return [];
    }

    private static IEnumerable<string> EnumerateConfinedFiles(
        string searchDir,
        string searchPattern,
        bool recursive,
        string baseFull)
    {
        var pending = new Stack<string>();
        pending.Push(searchDir);
        while (pending.TryPop(out var current))
        {
            if (!IsUnderBase(current, baseFull))
                continue;

            foreach (var file in Directory.EnumerateFiles(current, searchPattern, SearchOption.TopDirectoryOnly))
            {
                if (IsUnderBase(file, baseFull))
                    yield return file;
            }

            if (!recursive)
                continue;

            foreach (var directory in Directory.EnumerateDirectories(current))
            {
                if (IsUnderBase(directory, baseFull))
                    pending.Push(directory);
            }
        }
    }

    private static bool IsUnderBase(string candidate, string baseFull)
    {
        var full = Path.GetFullPath(candidate);
        if (!full.StartsWith(baseFull + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(full, baseFull, StringComparison.Ordinal))
            return false;

        var relative = Path.GetRelativePath(baseFull, full);
        if (relative == ".")
            return !IsReparsePoint(baseFull);

        var cursor = baseFull;
        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            cursor = Path.Combine(cursor, segment);
            if ((File.Exists(cursor) || Directory.Exists(cursor)) && IsReparsePoint(cursor))
                return false;
        }
        return true;
    }

    private static bool IsReparsePoint(string path)
        => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}
