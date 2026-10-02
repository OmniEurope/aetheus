// SPDX-License-Identifier: EUPL-1.2
using System.Text;

namespace Aetheus.Shared.Components.Pipelines;

public static class PipelineGitPathPolicy
{
    public static string Slugify(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var character in name.Trim().ToLowerInvariant())
            builder.Append(char.IsLetterOrDigit(character) ? character : '-');
        var slug = builder.ToString().Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        return string.IsNullOrEmpty(slug) ? "pipeline" : slug;
    }
}
