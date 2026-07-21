// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Constants;

namespace Aetheus.Agent.Core.Services;

/// <summary>
/// Rewrites an internal-mirror clone URL's authority (scheme://host:port) onto the agent's OWN
/// reachable backend base - its configured <c>ServerUrl</c>, the exact endpoint the agent already uses
/// (and is proven to reach) for heartbeat/API. The <c>/git/{projectId}/{slug}.git</c> path is kept.
/// <para>
/// This makes the pipeline checkout clone from wherever the agent actually reaches the backend, with
/// ZERO server-side <c>GitLight:CloneBaseUrl</c> configuration, on any topology: a containerised VPS-sim
/// (agent base <c>host.docker.internal</c>), an SSH-tunnelled remote VPS (agent base <c>localhost</c>),
/// or a direct public host. The backend still freezes/re-homes an authority at dispatch, but the agent
/// has the ground truth for reachability, so it gets the final say.
/// </para>
/// Non-mirror URLs (external repos - GitHub/GitLab/self-hosted) are returned unchanged, matching the
/// exact mirror-path shape guard used server-side (<c>MirrorCloneUrl</c>).
/// </summary>
internal static class MirrorUrlRehomer
{
    public static string RehomeToAgentBase(string url, string agentServerUrl)
    {
        if (string.IsNullOrWhiteSpace(url)
            || string.IsNullOrWhiteSpace(agentServerUrl)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !Uri.TryCreate(agentServerUrl, UriKind.Absolute, out var baseUri)
            || !MirrorRepoPath.IsMirrorPath(uri.AbsolutePath))
        {
            return url;
        }

        // Swap ONLY the authority to the agent's reachable base; keep userinfo (embedded git creds, if
        // any), path, and query untouched.
        var builder = new UriBuilder(uri)
        {
            Scheme = baseUri.Scheme,
            Host = baseUri.Host,
            Port = baseUri.IsDefaultPort ? -1 : baseUri.Port,
        };
        return builder.Uri.ToString();
    }
}
