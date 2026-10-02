// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.Pipelines;

/// <summary>
/// Validates the functional readiness URL used by a typed deployment. The probe runs on the target
/// agent, so only loopback HTTP(S) endpoints are accepted: a pipeline must not turn the deployment
/// agent into an SSRF proxy for the host network.
/// </summary>
public static class DeployHealthUrlValidator
{
    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !uri.IsLoopback
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        normalized = uri.AbsoluteUri;
        return true;
    }
}
