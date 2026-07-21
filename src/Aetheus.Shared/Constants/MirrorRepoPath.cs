// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;

namespace Aetheus.Shared.Constants;

/// <summary>
/// S-TECH-MURL: single source of truth for the internal smart-HTTP mirror clone-URL <em>path</em> shape
/// (<c>/git/{projectId:int&gt;0}/{slug}.git</c>). The backend (<c>MirrorCloneUrl</c>) and the agent
/// (<c>MirrorUrlRehomer</c>) both need to (a) build this path and (b) recognise it exactly before
/// re-homing a URL's authority; they parsed it independently and could drift. Both now call here.
/// </summary>
public static class MirrorRepoPath
{
    /// <summary>Builds the normalized mirror path (no leading slash): <c>git/{projectId}/{slug}.git</c>.</summary>
    public static string Build(int projectId, string slug) => $"git/{projectId}/{slug}.git";

    /// <summary>
    /// Confirms <paramref name="absolutePath"/> is one of our mirror paths (exact shape
    /// <c>/git/{projectId:int&gt;0}/{slug}.git</c>) and returns the normalized <c>git/{projectId}/{slug}.git</c>
    /// path (no leading slash). Requiring the middle segment to parse as a positive int keeps callers from
    /// rewriting any incidental third-party URL that merely happens to match the <c>/git/x/y.git</c> shape.
    /// </summary>
    public static bool TryParse(string absolutePath, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrEmpty(absolutePath)) return false;

        var segments = absolutePath.Trim('/').Split('/');
        if (segments.Length != 3
            || !segments[0].Equals("git", StringComparison.Ordinal)
            || !segments[2].EndsWith(".git", StringComparison.Ordinal)
            || !int.TryParse(segments[1], NumberStyles.None, CultureInfo.InvariantCulture, out var projectId)
            || projectId <= 0)
        {
            return false;
        }

        normalizedPath = $"{segments[0]}/{segments[1]}/{segments[2]}";
        return true;
    }

    /// <summary>Shape-only check for callers that do not need the normalized path.</summary>
    public static bool IsMirrorPath(string absolutePath) => TryParse(absolutePath, out _);
}
