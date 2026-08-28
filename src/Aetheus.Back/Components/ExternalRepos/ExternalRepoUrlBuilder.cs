// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.ExternalRepos;

/// <summary>
/// Builds the credential-free remote URL of an external repository from its provider, optional
/// self-hosted base URL, owner/group and repository name. HTTPS for token auth, SSH (scp-like) for
/// key auth. The credential itself is never part of the URL - it is injected by
/// <see cref="CreditedGitRunner"/>.
/// </summary>
public static class ExternalRepoUrlBuilder
{
    public static string Build(
        GitProviderType provider, string? baseUrl, string ownerOrGroup, string repositoryName, GitAuthType authType)
    {
        var owner = ownerOrGroup.Trim().Trim('/');
        var repo = repositoryName.Trim().Trim('/');
        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            repo = repo[..^4];
        }

        var host = ResolveHost(provider, baseUrl);

        return authType == GitAuthType.Ssh
            ? $"git@{host}:{owner}/{repo}.git"
            : $"https://{host}/{owner}/{repo}.git";
    }

    private static string ResolveHost(GitProviderType provider, string? baseUrl)
    {
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            // Self-hosted Gitea/GitLab/etc. Accept either a bare host or a full URL.
            var trimmed = baseUrl.Trim();
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            {
                return uri.Host;
            }

            return trimmed.TrimEnd('/');
        }

        return provider switch
        {
            GitProviderType.GitHub => "github.com",
            GitProviderType.GitLab => "gitlab.com",
            _ => throw new BadRequestException(
                "A base URL is required for self-hosted Git providers (Gitea / self-hosted)."),
        };
    }
}
