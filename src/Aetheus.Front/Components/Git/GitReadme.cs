// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Components.Git;

/// <summary>
/// The README of a repository folder, rendered for the page's README tab through the pipeline the page
/// passes (one built with DisableHtml, so raw HTML never reaches the MarkupString). Extracted from
/// <see cref="GitRepositoryDetail"/> to keep its code-behind within the per-file size budget.
/// </summary>
internal static class GitReadme
{
    /// <summary>The README.md among <paramref name="entries"/>, its text and HTML; nulls when there is
    /// none or it is binary.</summary>
    public static async Task<(string? Content, string? Html)> LoadAsync(
        GitApi git, int repoId, string refName, IEnumerable<GitLightTreeEntryDto> entries,
        Markdig.MarkdownPipeline pipeline)
    {
        var readme = entries.FirstOrDefault(e =>
            e.Type == GitTreeEntryType.Blob &&
            e.Name.Equals("README.md", StringComparison.OrdinalIgnoreCase));
        if (readme is null) return (null, null);
        var blob = await git.GetGitBlobAsync(repoId, refName, readme.Path);
        if (blob is null || blob.IsBinary) return (null, null);
        return (blob.Content, Markdig.Markdown.ToHtml(blob.Content ?? string.Empty, pipeline));
    }
}
