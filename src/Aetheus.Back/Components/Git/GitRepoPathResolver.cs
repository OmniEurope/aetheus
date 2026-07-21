// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;

namespace Aetheus.Back.Components.Git;

/// <summary>
/// Single source of truth for resolving an internal git repository's on-disk path from
/// (projectId, slug), with the path-traversal guard applied. Factored out of the three former
/// copies (M-git-6): <c>GitLightService.ResolveDiskPath</c>, <c>GitSmartHttpService</c>, and
/// <c>ExternalRepoMirrorService.ResolveMirrorPath</c>, which had drifted between <c>Ordinal</c> and
/// <c>OrdinalIgnoreCase</c>. The comparator is now OS-aware (case-insensitive on Windows where the
/// filesystem is, case-sensitive on Linux) - a single documented choice instead of three.
/// </summary>
internal static class GitRepoPathResolver
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// Returns the canonical <c>{root}/{projectId}/{slug}.git</c> path if it stays under
    /// <paramref name="repositoriesPath"/> (prefix + separator check defeats the sibling-prefix
    /// attack, e.g. <c>repos-evil</c>), otherwise <c>null</c>. Callers choose their own failure policy.
    /// </summary>
    public static string? TryResolve(string repositoriesPath, int projectId, string slug)
    {
        var root = Path.GetFullPath(repositoriesPath);
        var candidate = Path.GetFullPath(Path.Combine(root, projectId.ToString(CultureInfo.InvariantCulture), $"{slug}.git"));
        var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootWithSep, PathComparison) ? candidate : null;
    }
}
