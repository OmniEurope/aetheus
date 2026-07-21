// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// Decides whether a pushed git ref satisfies a pipeline's webhook <c>branches:</c> filter.
/// An empty filter matches everything (the pre-filter behaviour). A non-empty filter matches when the
/// branch (the ref with its <c>refs/heads/</c> prefix stripped) equals one of the patterns exactly, or
/// matches a glob. Glob semantics follow GitHub Actions <c>on.push.branches</c>: <c>*</c> matches any run
/// of characters WITHIN a path segment (it does not cross <c>/</c>), and <c>**</c> matches across segments
/// (so <c>release/*</c> matches <c>release/1.0</c> but not <c>release/1.0/rc1</c>, whereas <c>release/**</c>
/// matches both). A pattern may itself carry a leading <c>refs/heads/</c> (stripped before comparison, so a
/// copy-pasted fully-qualified ref still works). Matching is case-sensitive (git branch names are). A tag
/// push (<c>refs/tags/...</c>) never satisfies a non-empty branch filter.
/// </summary>
internal static class PipelineBranchFilter
{
    // Real bound, not decoration: a pattern with repeated wildcards (e.g. `*a*a*a*a*b`) compiles to
    // alternating `[^/]*`/`.*` segments that CAN backtrack super-linearly against a long non-matching
    // branch, so the timeout caps a pathological author-supplied pattern.
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    private const string HeadsPrefix = "refs/heads/";

    /// <summary>
    /// Returns true when <paramref name="gitRef"/> should trigger a pipeline carrying the given branch
    /// <paramref name="patterns"/>. Empty/whitespace patterns are ignored; an all-empty filter matches.
    /// </summary>
    public static bool Matches(IReadOnlyList<string>? patterns, string? gitRef, ILogger? logger = null)
    {
        // No effective filter => fire on any branch (backward-compatible with the pre-filter webhook).
        var effective = patterns?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? [];
        if (effective.Count == 0) return true;

        var branch = NormalizeBranch(gitRef);
        // A non-branch ref (tag, or empty) can't match a branch filter that the author explicitly set.
        if (string.IsNullOrEmpty(branch)) return false;

        foreach (var pattern in effective)
        {
            // Accept a fully-qualified pattern too: `refs/heads/main` in YAML is a natural copy-paste
            // from a provider's docs and must gate the same branch as a bare `main`.
            var trimmed = StripHeads(pattern.Trim());
            if (trimmed.Contains('*', StringComparison.Ordinal))
            {
                if (GlobMatches(trimmed, branch, logger)) return true;
            }
            else if (string.Equals(trimmed, branch, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Best-effort textual check for whether raw pipeline YAML declares a top-level <c>branches:</c>
    /// key. Used ONLY as a fail-closed signal by the webhook trigger when structured parsing
    /// (<see cref="Aetheus.Back.Services.YamlParsingHelper.ParseAndValidate"/>) has already failed and
    /// the real pattern list can no longer be recovered: a false positive (e.g. the text "branches:"
    /// appears inside an unrelated script string) merely skips the pipeline once instead of running it
    /// on every branch - the safe direction to be wrong in.
    /// </summary>
    public static bool DeclaresFilter(string? yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml)) return false;
        try
        {
            return Regex.IsMatch(yaml, @"^\s*branches\s*:", RegexOptions.Multiline | RegexOptions.CultureInvariant, MatchTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            // Same defensive stance as GlobMatches: a timeout must not bubble out of the webhook loop and
            // abort every remaining pipeline. Treat it as "declares a filter" - the fail-closed direction.
            return true;
        }
    }

    private static string StripHeads(string value) =>
        value.StartsWith(HeadsPrefix, StringComparison.Ordinal) ? value[HeadsPrefix.Length..] : value;

    // A branch filter targets branches only. Strip refs/heads/ to the bare branch name; a fully-qualified
    // non-branch ref (refs/tags/..., refs/notes/...) yields an empty name so it can never match a filter
    // (even a `*` glob), i.e. tag pushes never trigger a branch-filtered pipeline. A bare, unqualified
    // name (no refs/ prefix) is taken as the branch as-is.
    private static string NormalizeBranch(string? gitRef)
    {
        if (string.IsNullOrWhiteSpace(gitRef)) return string.Empty;
        var r = gitRef.Trim();
        if (r.StartsWith(HeadsPrefix, StringComparison.Ordinal)) return r[HeadsPrefix.Length..];
        if (r.StartsWith("refs/", StringComparison.Ordinal)) return string.Empty; // tag / note / other non-branch ref
        return r;
    }

    private static bool GlobMatches(string pattern, string branch, ILogger? logger)
    {
        // Escape every regex metacharacter, then re-open the wildcards with GitHub Actions semantics:
        // `**` (escaped to `\*\*`) becomes `.*` (crosses '/'), and a single `*` becomes `[^/]*` (bounded
        // to one path segment). Order matters - collapse the `\*\*` pairs BEFORE the lone `\*`.
        var body = Regex.Escape(pattern)
            .Replace("\\*\\*", ".*", StringComparison.Ordinal)
            .Replace("\\*", "[^/]*", StringComparison.Ordinal);
        var regex = "\\A" + body + "\\z";
        try
        {
            return Regex.IsMatch(branch, regex, RegexOptions.CultureInvariant, MatchTimeout);
        }
        catch (RegexMatchTimeoutException ex)
        {
            // A pathological author-supplied pattern hit the timeout guard - fail the match (safe
            // direction) but log it so a runaway glob pattern doesn't silently stop firing forever.
            logger?.LogWarning(ex, "Branch filter pattern '{Pattern}' timed out matching '{Branch}'", pattern, branch);
            return false;
        }
    }
}
