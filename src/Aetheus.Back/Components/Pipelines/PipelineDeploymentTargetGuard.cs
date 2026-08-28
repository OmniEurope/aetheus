// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// Keeps the opt-in local self-deploy campaign physically isolated from production.
/// Production remains the default; local runs are accepted only with loopback-only
/// domains/endpoints and a dedicated simulated agent.
/// </summary>
internal static class PipelineDeploymentTargetGuard
{
    internal const string TargetVariable = "AETHEUS_DEPLOY_TARGET";
    internal const string LocalAgentVariable = "AETHEUS_LOCAL_AGENT";
    internal const string Production = "production";
    internal const string Local = "local";

    private static readonly string[] ForbiddenLocalTokens =
    [
        "sonytumen.com",
        "vps2577917",
        "aetheus-prod"
    ];

    private static readonly string[] EndpointKeys =
    [
        "REPOSITORY_URL",
        "BUILD_REPOSITORY_URI",
        "PUBLIC_APP_URL",
        "PUBLIC_API_URL"
    ];

    private static readonly string[] DomainKeys =
    [
        "SITE_DOMAIN",
        "APP_HOST",
        "API_HOST"
    ];

    internal static void ValidateVariables(IReadOnlyDictionary<string, string> variables)
    {
        if (!variables.TryGetValue(TargetVariable, out var target) || string.IsNullOrWhiteSpace(target))
            return;

        if (target.Equals(Production, StringComparison.OrdinalIgnoreCase))
            return;

        if (!target.Equals(Local, StringComparison.OrdinalIgnoreCase))
            throw new BadRequestException($"Unsupported {TargetVariable} value '{target}'. Expected '{Production}' or '{Local}'.");
        ValidateNoProductionValues(variables);
        ValidateLocalEndpoints(variables);
        ValidateLocalDomains(variables);
        ValidateLocalRuntimeOptions(variables);
    }

    private static void ValidateNoProductionValues(IReadOnlyDictionary<string, string> variables)
    {
        foreach (var (key, value) in variables)
        {
            var forbidden = ForbiddenLocalTokens.FirstOrDefault(token =>
                value.Contains(token, StringComparison.OrdinalIgnoreCase));
            if (forbidden is not null)
                throw new BadRequestException($"Local deployment target variable '{key}' contains forbidden production value '{forbidden}'.");
        }
    }

    private static void ValidateLocalEndpoints(IReadOnlyDictionary<string, string> variables)
    {
        foreach (var key in EndpointKeys)
        {
            if (!variables.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)) continue;
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !IsLocalHost(uri.Host))
            {
                throw new BadRequestException($"Local deployment target variable '{key}' must be an HTTPS URL on a local-only host.");
            }
        }
    }

    private static void ValidateLocalDomains(IReadOnlyDictionary<string, string> variables)
    {
        foreach (var key in DomainKeys)
        {
            if (variables.TryGetValue(key, out var value)
                && !string.IsNullOrWhiteSpace(value)
                && !IsLocalHost(value))
            {
                throw new BadRequestException($"Local deployment target variable '{key}' must use a local-only host name.");
            }
        }
    }

    private static void ValidateLocalRuntimeOptions(IReadOnlyDictionary<string, string> variables)
    {
        if (variables.TryGetValue("CERTBOT_MODE", out var certbotMode)
            && !certbotMode.Equals(Local, StringComparison.OrdinalIgnoreCase))
            throw new BadRequestException("A local deployment target must set CERTBOT_MODE to 'local'.");

        if (variables.TryGetValue("TLS_CA_FILE", out var tlsCaFile)
            && string.IsNullOrWhiteSpace(tlsCaFile))
            throw new BadRequestException("A local deployment target must provide TLS_CA_FILE for explicit certificate trust.");

        if (!variables.TryGetValue(LocalAgentVariable, out var localAgent)
            || string.IsNullOrWhiteSpace(localAgent))
            throw new BadRequestException(
                $"A local deployment target requires an explicit runner selector in {LocalAgentVariable}.");
    }

    internal static string? ValidateServer(IReadOnlyDictionary<string, string> variables, Server server)
    {
        if (!variables.TryGetValue(TargetVariable, out var target)
            || !target.Equals(Local, StringComparison.OrdinalIgnoreCase))
            return null;

        if (!variables.TryGetValue(LocalAgentVariable, out var localAgent)
            || string.IsNullOrWhiteSpace(localAgent))
            return $"Local deployment target refused runner '{server.Name}' ({server.Hostname}); an explicit runner selector is required.";

        return server.Name.Equals(localAgent, StringComparison.OrdinalIgnoreCase)
               || server.Hostname.Equals(localAgent, StringComparison.OrdinalIgnoreCase)
            ? null
            : $"Local deployment target refused runner '{server.Name}' ({server.Hostname}); it does not match selector '{localAgent}'.";
    }

    private static bool IsLocalHost(string host)
        => host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || host.Equals("::1", StringComparison.OrdinalIgnoreCase)
            || host.Equals("host.docker.internal", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".test", StringComparison.OrdinalIgnoreCase);
}
