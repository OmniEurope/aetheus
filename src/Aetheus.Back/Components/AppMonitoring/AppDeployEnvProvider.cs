// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.AppMonitoring.Ingest;
using Aetheus.Back.Services;

namespace Aetheus.Back.Components.AppMonitoring;

public sealed class AppDeployEnvProvider(
    IAppMonitoringRepository appRepo,
    IngestKeyHasher hasher,
    ISecretMaskingService masking,
    IConfiguration configuration,
    TimeProvider timeProvider) : IAppDeployEnvProvider
{
    private static readonly IReadOnlyDictionary<string, string> None = new Dictionary<string, string>();

    public async Task<IReadOnlyDictionary<string, string>> GetDeployEnvAsync(
        int projectId, int? environmentId, int pipelineRunId, CancellationToken ct = default)
    {
        // Feature is opt-in per environment: without a configured public OTLP base URL there is nowhere
        // for the deployed app to push, so we inject nothing (a bogus endpoint would be worse than none).
        var baseUrl = configuration["AppMonitoring:IngestBaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            return None;

        var app = await appRepo.GetDeployTargetAppAsync(projectId, environmentId, ct).ConfigureAwait(false);
        if (app is null)
            return None;

        // The stored key is a one-way hash - we can't recover a previously issued plaintext, so each deploy
        // issues (rotates to) a fresh key and injects it. The deployed app always gets a currently-valid key.
        var plaintext = hasher.Generate();
        app.IngestKeyHash = hasher.Hash(plaintext);
        app.IngestKeyCreatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await appRepo.SaveChangesAsync(ct).ConfigureAwait(false);

        // Mask the plaintext in this run's step logs (it will appear in the env dispatch).
        masking.RegisterRuntimeSecret(pipelineRunId, plaintext);

        var endpoint = baseUrl.TrimEnd('/') + "/api/ingest/otlp/v1";
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = endpoint,
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/json",
            ["OTEL_EXPORTER_OTLP_HEADERS"] = $"x-aetheus-ingest-key={plaintext}"
        };
    }
}
