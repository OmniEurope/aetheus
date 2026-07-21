// SPDX-License-Identifier: EUPL-1.2
using System.Text;
using Aetheus.Shared.DTOs;

namespace Aetheus.Front.Pages.Git;

/// <summary>
/// Pure view helpers extracted from <see cref="GitRepositoryDetail"/> to keep the code-behind
/// within the per-file size budget without a <c>partial</c> split.
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

    public static string InferLanguage(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
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
            ".py" => "python",
            ".sh" or ".bash" => "shell",
            ".ps1" => "powershell",
            ".sql" => "sql",
            ".dockerfile" => "dockerfile",
            ".razor" => "razor",
            _ => "plaintext"
        };
    }
}
