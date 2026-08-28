// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Extensions;

namespace Aetheus.Back.Components.ExternalRepos;

/// <summary>
/// Single source of truth for the internal smart-HTTP clone URL of an external-repo mirror. Both the
/// attach path (<see cref="ExternalRepoService"/>) and the clone-resolve path
/// (<see cref="RepoSourceResolver"/>) build it here so the stored URL and the recomputed URL can never
/// diverge in dev / behind a proxy. Resolution is config-first (<c>GitLight:CloneBaseUrl</c>, then
/// <c>Aetheus:PublicApiBaseUrl</c> per <see cref="HostUrlExtensions.ResolvePublicApiUrl"/>); the
/// request host is only a dev fallback used when an HttpContext is available and no canonical URL
/// is configured.
/// </summary>
internal static class MirrorCloneUrl
{
    public static string Build(IConfiguration config, HttpContext? httpContext, int projectId, string slug)
    {
        var cloneBaseUrl = config["GitLight:CloneBaseUrl"];
        var baseUrl = !string.IsNullOrWhiteSpace(cloneBaseUrl)
            ? cloneBaseUrl.TrimEnd('/')
            : httpContext is not null
                ? HostUrlExtensions.ResolvePublicApiUrl(config, httpContext)
                : (config["Aetheus:PublicApiBaseUrl"] ?? string.Empty).TrimEnd('/');

        return $"{baseUrl}/{MirrorRepoPath.Build(projectId, slug)}";
    }

    /// <summary>
    /// Re-homes a previously-persisted internal-mirror clone URL onto the executing backend's
    /// configured clone base at dispatch time. <c>Project.RepositoryUrl</c> freezes the host:port at
    /// attach time, which is wrong for any consumer reaching the backend from elsewhere - a remote
    /// agent, a containerised agent (where <c>localhost</c> is the container, not the host), or simply
    /// after a redeploy moved the public URL. We keep the <c>/git/{projectId}/{slug}.git</c> path and
    /// swap only the authority. The base is <c>GitLight:CloneBaseUrl</c> (a dedicated knob so dev can
    /// point agents at a reachable, plain-HTTP host that sidesteps the self-signed dev cert) falling
    /// back to <c>Aetheus:PublicApiBaseUrl</c>. Inputs that are not one of our smart-HTTP mirror
    /// URLs (exact shape <c>/git/{projectId:int}/{slug}.git</c>), or when no base is configured, are
    /// returned unchanged.
    /// </summary>
    public static string? Rehome(IConfiguration config, string? storedUrl)
    {
        if (string.IsNullOrWhiteSpace(storedUrl)
            || !Uri.TryCreate(storedUrl, UriKind.Absolute, out var uri)
            || !MirrorRepoPath.TryParse(uri.AbsolutePath, out var path))
        {
            return storedUrl;
        }

        var baseUrl = (config["GitLight:CloneBaseUrl"] ?? config["Aetheus:PublicApiBaseUrl"] ?? string.Empty).TrimEnd('/');
        return string.IsNullOrEmpty(baseUrl)
            ? storedUrl
            : $"{baseUrl}/{path}";
    }
}
