// SPDX-License-Identifier: EUPL-1.2
using Microsoft.Extensions.Options;

namespace Aetheus.Agent.Core.Configuration;

internal sealed class AetheusAgentOptionsValidator : IValidateOptions<AetheusAgentOptions>
{
    public ValidateOptionsResult Validate(string? name, AetheusAgentOptions options)
    {
        var failures = new List<string>();

        ValidateRange(failures, nameof(options.PollingIntervalSeconds), options.PollingIntervalSeconds, 1, 3600);
        ValidateRange(failures, nameof(options.HeartbeatIntervalSeconds), options.HeartbeatIntervalSeconds, 1, 3600);
        ValidateRange(failures, nameof(options.HeartbeatCollectionTimeoutSeconds), options.HeartbeatCollectionTimeoutSeconds, 1, 3600);
        ValidateRange(failures, nameof(options.MaxConcurrentTasks), options.MaxConcurrentTasks, 1, 10);
        ValidateRange(failures, nameof(options.LogRetentionDays), options.LogRetentionDays, 1, 3650);
        ValidateRange(failures, nameof(options.MinTimeoutSeconds), options.MinTimeoutSeconds, 1, 86400);
        ValidateRange(failures, nameof(options.MaxTimeoutSeconds), options.MaxTimeoutSeconds, 1, 86400);
        ValidateRange(failures, nameof(options.DeployReleasesToKeep), options.DeployReleasesToKeep, 1, 100);
        ValidateRange(failures, nameof(options.DeployMinFreeSpaceMiB), options.DeployMinFreeSpaceMiB, 1, 1_048_576);
        ValidateRange(failures, nameof(options.DeployUnpackRatioEstimate), options.DeployUnpackRatioEstimate, 1, 100);

        if (options.MaxTimeoutSeconds < options.MinTimeoutSeconds)
            failures.Add($"{nameof(options.MaxTimeoutSeconds)} must be greater than or equal to {nameof(options.MinTimeoutSeconds)}.");
        if (string.IsNullOrWhiteSpace(options.WorkDirectory))
            failures.Add($"{nameof(options.WorkDirectory)} must not be empty.");
        if (string.IsNullOrWhiteSpace(options.PluginDirectory))
            failures.Add($"{nameof(options.PluginDirectory)} must not be empty.");
        if (!string.IsNullOrWhiteSpace(options.PinnedServerCertThumbprint)
            && (options.PinnedServerCertThumbprint.Replace(":", "", StringComparison.Ordinal).Length != 64
                || !options.PinnedServerCertThumbprint.Replace(":", "", StringComparison.Ordinal).All(Uri.IsHexDigit)))
        {
            failures.Add($"{nameof(options.PinnedServerCertThumbprint)} must be a SHA-256 hexadecimal thumbprint.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateRange(List<string> failures, string property, int value, int minimum, int maximum)
    {
        if (value < minimum || value > maximum)
            failures.Add($"{property} must be between {minimum} and {maximum}.");
    }
}
