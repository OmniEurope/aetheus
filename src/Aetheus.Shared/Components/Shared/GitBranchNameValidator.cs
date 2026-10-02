// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Shared.Components.Shared;

/// <summary>
/// A conservative subset of <c>git check-ref-format --branch</c>, applied before a branch name ever
/// reaches git's argv. It is deliberately stricter than git itself: this validates names Aetheus
/// itself writes into refs, not arbitrary names it has to accept from an existing repository.
/// </summary>
public static class GitBranchNameValidator
{
    /// <summary>Longer than any branch name a human writes, and short enough to stay well inside the
    /// filesystem's path budget once it becomes <c>.git/refs/heads/&lt;name&gt;</c>.</summary>
    public const int MaxLength = 255;

    public static bool IsValid(string? branch)
    {
        if (string.IsNullOrWhiteSpace(branch)) return false;
        if (branch.Length > MaxLength) return false;

        // A leading '-' would be read by git as an option rather than a ref (argument injection).
        if (branch[0] is '-' or '.' or '/') return false;
        if (branch[^1] is '.' or '/') return false;
        if (branch.EndsWith(".lock", StringComparison.Ordinal)) return false;
        if (branch.Contains("..", StringComparison.Ordinal)) return false;
        if (branch.Contains("//", StringComparison.Ordinal)) return false;
        if (branch.Contains("@{", StringComparison.Ordinal)) return false;
        if (branch == "@") return false;

        foreach (var c in branch)
        {
            if (char.IsControl(c)) return false;
            if (c is ' ' or '~' or '^' or ':' or '?' or '*' or '[' or '\\') return false;
            if (c > 0x7E) return false;
        }
        return true;
    }
}
