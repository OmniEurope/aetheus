// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Components.Releases;

internal static class ReleaseHelper
{
    internal static OmniTone GetReleaseBadge(ReleaseStatus status) => status switch
    {
        ReleaseStatus.Detected => OmniTone.Accent,
        ReleaseStatus.Building => OmniTone.Warning,
        ReleaseStatus.Published => OmniTone.Success,
        ReleaseStatus.Deployed => OmniTone.Success,
        ReleaseStatus.Failed => OmniTone.Danger,
        ReleaseStatus.RolledBack => OmniTone.Neutral,
        ReleaseStatus.Promoted => OmniTone.Accent,
        ReleaseStatus.Superseded => OmniTone.Neutral,
        _ => OmniTone.Neutral
    };

    /// <summary>
    /// PLAN-005 D39: the one short form of a release name, everywhere. A candidate is named
    /// <c>c-</c> plus the full 40-character commit SHA, which is exact and unreadable on screen:
    /// since recette R-373 it becomes <c>c-384f63d4</c>, the prefix and the first eight characters,
    /// the rule <see cref="ShortId"/> applies to every opaque id (a bare SHA included). A readable
    /// version (a semantic version, a short name) is returned as it is. The full name stays in a
    /// <c>title</c> wherever this is rendered.
    /// </summary>
    internal static string ShortVersion(string? version) => ShortId.Shorten(version);

    /// <summary>
    /// When a release came to be: its publication, else its detection. A release a pipeline published
    /// was never "detected", and older ones kept an empty detection date that rendered as 01/01/0001
    /// (the release grid of the run dialog, measured 2026-09-11). Browser-local deserialization can
    /// shift a wire MinValue, so a year below 2000 is the sentinel. Null when neither is known.
    /// </summary>
    internal static DateTime? CreatedAt(ReleaseDto release) => release.CreatedAt;
}
