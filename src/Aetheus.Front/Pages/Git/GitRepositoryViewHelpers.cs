// SPDX-License-Identifier: EUPL-1.2
using System.Text;

namespace Aetheus.Front.Pages.Git;

/// <summary>
/// Pure view helpers extracted from <see cref="GitRepositoryDetail"/> to keep the code-behind
/// within the per-file size budget without a <c>partial</c> split (see CLAUDE.md "Interdictions").
/// Stateless string/pattern formatting only - no component state.
/// </summary>
internal static class GitRepositoryViewHelpers
{
    public static bool BranchMatchesPattern(string branchName, string pattern)
    {
        if (pattern == "*") return true;
        if (pattern.EndsWith('*'))
            return branchName.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);
        return string.Equals(branchName, pattern, StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatBlame(List<GitLightBlameLine> lines)
    {
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            var author = line.AuthorName.Length > 15 ? line.AuthorName[..15] : line.AuthorName.PadRight(15);
            sb.AppendLine($"{line.ShortSha} {author} {line.AuthorDate:yyyy-MM-dd} │ {line.Line}");
        }
        return sb.ToString();
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
    };

    public static BadgeStyle GetPullRequestStatusBadge(PullRequestStatus status) => status switch
    {
        PullRequestStatus.Open => BadgeStyle.Success,
        PullRequestStatus.Merged => BadgeStyle.Primary,
        PullRequestStatus.Closed => BadgeStyle.Danger,
        PullRequestStatus.Draft => BadgeStyle.Light,
        _ => BadgeStyle.Light
    };

    public static string InferLanguage(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return InferCommonLanguage(ext) ?? InferSpecializedLanguage(ext) ?? "plaintext";
    }

    private static string? InferCommonLanguage(string extension) => extension switch
    {
        ".cs" => "csharp",
        ".js" => "javascript",
        ".ts" => "typescript",
        ".json" => "json",
        ".xml" or ".csproj" or ".sln" or ".props" => "xml",
        ".yaml" or ".yml" => "yaml",
        ".html" or ".htm" => "html",
        ".css" => "css",
        ".scss" => "scss",
        ".md" => "markdown",
        _ => null
    };

    private static string? InferSpecializedLanguage(string extension) => extension switch
    {
        ".py" => "python",
        ".sh" or ".bash" => "shell",
        ".ps1" => "powershell",
        ".sql" => "sql",
        ".dockerfile" => "dockerfile",
        ".razor" => "razor",
        _ => null
    };
}
