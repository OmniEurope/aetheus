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

            var searchOption = pattern.Contains("**") ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            return Directory.EnumerateFiles(fullSearchDir, searchPattern, searchOption)
                .Where(file => IsUnderBase(file, baseFull));
        }

        var fullPath = Path.Combine(baseDir, pattern);
        if (!IsUnderBase(fullPath, baseFull)) return [];
        if (File.Exists(fullPath)) return [fullPath];
        if (Directory.Exists(fullPath)) return Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories);
        return [];
    }

    private static bool IsUnderBase(string candidate, string baseFull)
    {
        var full = Path.GetFullPath(candidate);
        return full.StartsWith(baseFull + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || string.Equals(full, baseFull, StringComparison.Ordinal);
    }
}
