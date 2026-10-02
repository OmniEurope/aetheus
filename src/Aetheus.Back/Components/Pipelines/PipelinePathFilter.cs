// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// Decides whether a push changed anything the pipeline cares about, from the definition's
/// <c>paths_ignore:</c> list.
///
/// This is the honest answer to "a documentation push kills the running candidate". The pipeline
/// declares <c>supersede_running: true</c> on purpose, so every push replaces the candidate in
/// flight; the cost that was worth avoiding is a two-hour qualification thrown away for a typo in a
/// README. Reusing another commit's artifacts would have avoided it too, and would have sealed a
/// release whose images were built from a different revision. Not starting a run the push cannot
/// affect costs nothing and lies about nothing.
///
/// Deliberately fail-open at every step. Skipping a run is invisible: nobody sees the build that did
/// not happen. So a push is skipped only when the changed paths are known in full and every one of
/// them matches; anything else runs.
/// </summary>
public static class PipelinePathFilter
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// True when this push may be skipped: the definition declares patterns, the changed paths were
    /// read in full, there is at least one, and every one matches a pattern.
    /// </summary>
    /// <param name="pathsIgnore">Patterns from the definition, GitHub Actions glob semantics.</param>
    /// <param name="changedPaths">Every path the push touched, or null when git could not say.</param>
    public static bool ShouldSkip(
        IReadOnlyList<string> pathsIgnore,
        IReadOnlyList<string>? changedPaths,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(pathsIgnore);
        if (pathsIgnore.Count == 0) return false;

        // Unknown is not "nothing changed". A push whose diff could not be read must build.
        if (changedPaths is null || changedPaths.Count == 0) return false;

        return changedPaths.All(path =>
            pathsIgnore.Any(pattern => GlobMatches(pattern, Normalize(path), logger)));
    }

    /// <summary>True when <paramref name="path"/> matches the glob <paramref name="pattern"/> (same
    /// semantics as <c>paths_ignore:</c>).</summary>
    internal static bool Matches(string pattern, string path) => GlobMatches(pattern, Normalize(path), logger: null);

    /// <summary>Git reports forward slashes and no leading separator; normalising here means a
    /// pattern author never has to think about the platform the control plane runs on.</summary>
    private static string Normalize(string path) =>
        path.Replace('\\', '/').TrimStart('/').Trim();

    // Same semantics as the branch filter, for the same reason: an author who learned one should not
    // have to learn a second. `**` crosses '/', a lone `*` stays inside one segment.
    //
    // One addition the branch filter does not need: a LEADING `**/` matches zero or more directories,
    // so `**/*.md` covers `README.md` at the root as well as `docs/a/b.md`. That is what the pattern
    // means to everyone who writes it, and reading it strictly would quietly exclude exactly the
    // root-level files a documentation filter is written for.
    private static bool GlobMatches(string pattern, string path, ILogger? logger)
    {
        var trimmed = pattern.Trim();
        var leadingAnyDirectory = trimmed.StartsWith("**/", StringComparison.Ordinal);
        if (leadingAnyDirectory) trimmed = trimmed[3..];

        var body = Regex.Escape(trimmed)
            .Replace("\\*\\*", ".*", StringComparison.Ordinal)
            .Replace("\\*", "[^/]*", StringComparison.Ordinal);
        if (leadingAnyDirectory) body = "(?:.*/)?" + body;
        try
        {
            return Regex.IsMatch(path, "\\A" + body + "\\z", RegexOptions.CultureInvariant, MatchTimeout);
        }
        catch (RegexMatchTimeoutException ex)
        {
            // Fail the match, which means "this path is not ignored", which means the run happens.
            logger?.LogWarning(ex, "paths_ignore pattern '{Pattern}' timed out matching '{Path}'", pattern, path);
            return false;
        }
    }
}
