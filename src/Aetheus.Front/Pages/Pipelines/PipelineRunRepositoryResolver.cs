// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;

namespace Aetheus.Front.Pages.Pipelines;

internal static class PipelineRunRepositoryResolver
{
    public static async Task<int?> ResolveAsync(ApiClient api, PipelineRunDto run, int projectId)
    {
        var source = await api.GetPipelineSourceAsync(run.PipelineId);
        if (source is { RepositoryId: > 0 }) return source.RepositoryId;
        if (string.IsNullOrWhiteSpace(run.RepositoryUrl)) return null;

        var expectedUrl = CanonicalUrl(run.RepositoryUrl);
        var matches = (await api.GetGitReposAsync(projectId))
            .Where(repo => CanonicalUrl(repo.CloneUrl) == expectedUrl)
            .Select(repo => repo.Id)
            .Distinct()
            .Take(2)
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private static string? CanonicalUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var canonical = value.Trim().Replace('\\', '/').TrimEnd('/');
        if (canonical.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) canonical = canonical[..^4];
        return canonical.ToLowerInvariant();
    }
}
