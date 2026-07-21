// SPDX-License-Identifier: EUPL-1.2
using System.Text;

namespace Aetheus.Front.Helpers;

/// <summary>
/// Mirrors the backend's git-strict storage path for project-owned pipelines
/// (<c>PipelineGitService</c>): their YAML lives at <c>.pipeline/&lt;slug&gt;.yaml</c> in the
/// project's internal repo. Used to surface the "git-managed" indicator (S-FEAT-19 / S-DES-24).
/// </summary>
public static class PipelineGitPath
{
    public static string ForPipeline(string name) => $".pipeline/{Slugify(name)}.yaml";

    private static string Slugify(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name.Trim().ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '-');
        var slug = sb.ToString().Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        return string.IsNullOrEmpty(slug) ? "pipeline" : slug;
    }
}
