// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Git;

/// <summary>Branch-name glob matching shared by the deletion guard and the protection-rule check.
/// "*" matches everything; a trailing "*" is a case-insensitive prefix match; otherwise an exact
/// case-insensitive comparison.</summary>
internal static class GitBranchPatternMatcher
{
    public static bool Matches(string branchName, string pattern)
    {
        if (pattern == "*") return true;
        if (pattern.EndsWith('*'))
            return branchName.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);
        return string.Equals(branchName, pattern, StringComparison.OrdinalIgnoreCase);
    }
}
