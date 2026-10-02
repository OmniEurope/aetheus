// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Front.Components.Pipelines;

/// <summary>
/// The CompatibilityMode, PreviousVersionTested and CompatibilityVerdict outputs that
/// <c>deploy/scripts/qa/persist-compatibility-mode.sh</c> publishes, read from the run's steps (the
/// last value of each wins). The known codes are given in words with what they prove; an unknown code
/// is shown as it is, never guessed.
/// </summary>
public partial class PipelineRunCompatibilityAttestation
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter, EditorRequired] public PipelineRunDto Run { get; set; } = default!;

    /// <summary>The run's repository when it resolved to one; the commit link falls back to the
    /// project's repository list.</summary>
    [Parameter] public int? RepoId { get; set; }

    /// <summary>What the script writes as PreviousVersionTested when there is no V-1 (bootstrap).</summary>
    internal const string NoPrevious = "false";

    private static readonly HashSet<string> ModeCodes = new(StringComparer.Ordinal) { "NMinusOne", "Bootstrap" };

    private static readonly HashSet<string> VerdictCodes = new(StringComparer.Ordinal)
    {
        "NMinusOneVerified", "NMinusOneDegraded", "CurrentVersionVerified", "CurrentVersionDegraded"
    };

    private Dictionary<string, string> _outputs = new(StringComparer.Ordinal);

    private string? Mode => _outputs.GetValueOrDefault("CompatibilityMode");

    private string? Previous => _outputs.GetValueOrDefault("PreviousVersionTested");

    private string? Verdict => _outputs.GetValueOrDefault("CompatibilityVerdict");

    /// <summary>The V-1 commit, when PreviousVersionTested holds one (the script writes a full SHA).</summary>
    private string? PreviousSha => Previous is { } value && CommitSha().IsMatch(value) ? value : null;

    private string? PreviousHref => PreviousSha is not { } sha
        ? null
        : RepoId is { } repoId
            ? $"/git-repositories/{repoId}/commits/{Uri.EscapeDataString(sha)}"
            : Run.ProjectId is { } projectId ? $"/git-repositories?projectId={projectId}" : null;

    protected override void OnParametersSet() =>
        _outputs = Run.Steps
            .SelectMany(step => step.OutputVariables)
            .Where(output => output.Key is "CompatibilityMode" or "PreviousVersionTested" or "CompatibilityVerdict")
            .GroupBy(output => output.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);

    private string Readable(HashSet<string> known, string keyPrefix, string code) =>
        known.Contains(code) ? L[keyPrefix + code].Value : code;

    private string? Hint(HashSet<string> known, string keyPrefix, string code) =>
        known.Contains(code) ? L[keyPrefix + code].Value : null;

    private static OmniTone VerdictVariant(string verdict) => verdict switch
    {
        "NMinusOneVerified" or "CurrentVersionVerified" => OmniTone.Success,
        "NMinusOneDegraded" or "CurrentVersionDegraded" => OmniTone.Warning,
        _ => OmniTone.Neutral
    };

    [GeneratedRegex("^[0-9a-fA-F]{7,64}$")]
    private static partial Regex CommitSha();
}
