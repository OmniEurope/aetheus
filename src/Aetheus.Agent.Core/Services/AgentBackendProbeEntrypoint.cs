// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Configuration;

namespace Aetheus.Agent.Core.Services;

public static class AgentBackendProbeEntrypoint
{
    public static Task<int?> TryRunAsync(
        string[] args,
        IConfiguration configuration,
        CancellationToken cancellationToken = default) =>
        TryRunAsync(args, configuration, BackendProbe.RunAsync, cancellationToken);

    internal static async Task<int?> TryRunAsync(
        string[] args,
        IConfiguration configuration,
        Func<string, string[], bool, CancellationToken, Task<int>> runProbe,
        CancellationToken cancellationToken = default)
    {
        if (!HasProbeArgument(args))
            return null;

        var section = AetheusAgentOptions.SectionName;
        var serverUrl = configuration[$"{section}:ServerUrl"] ?? string.Empty;
        var allowInsecure = string.Equals(
            configuration[$"{section}:AllowInsecureCerts"], "true", StringComparison.OrdinalIgnoreCase);

        return await runProbe(serverUrl, args, allowInsecure, cancellationToken).ConfigureAwait(false);
    }

    private static bool HasProbeArgument(string[] args) =>
        args.Any(argument => string.Equals(argument, "--probe-backend", StringComparison.OrdinalIgnoreCase));
}
