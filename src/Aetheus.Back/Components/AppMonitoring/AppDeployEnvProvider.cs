// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.AppMonitoring.Ingest;
using Aetheus.Back.Components.Vaults;
using Microsoft.Extensions.Caching.Memory;

namespace Aetheus.Back.Components.AppMonitoring;

public sealed class AppDeployEnvProvider(
    IAppMonitoringRepository appRepo,
    IngestKeyHasher hasher,
    ISecretMaskingService masking,
    IIngestService ingestService,
    IAuditService audit,
    IVaultService vaults,
    IConfiguration configuration,
    IMemoryCache cache,
    TimeProvider timeProvider) : IAppDeployEnvProvider
{
    private static readonly IReadOnlyDictionary<string, string> None = new Dictionary<string, string>();

    /// <summary>
    /// How long an issued key stays reusable within its run. Comfortably longer than a deployment,
    /// short enough that a plaintext does not linger in memory once the run is over.
    /// </summary>
    private static readonly TimeSpan RunKeyLifetime = TimeSpan.FromHours(6);

    private static string RunKeyCacheKey(int appId, int pipelineRunId) =>
        $"deploy-ingest-key:{appId}:{pipelineRunId}";

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

        // The stored key is a one-way hash - a previously issued plaintext cannot be recovered, so the
        // one issued for this run is kept in memory and reused by every later stage of the same run.
        //
        // Rotating per call looked right when only one stage deployed, but this method runs once per
        // stage carrying execution_role: deploy - about ten times in a production deployment. The
        // container starts at the Up stage holding the key of that moment, and rotation keeps exactly
        // one previous key: from the second rotation onwards, the key the container holds is neither
        // current nor previous. Every OTLP export then came back 401, which is why no application had
        // ever recorded a single data point.
        //
        // One key per run, not per stage: the container keeps a key that stays valid for the whole
        // deployment and beyond.
        if (!cache.TryGetValue(RunKeyCacheKey(app.Id, pipelineRunId), out string? plaintext)
            || string.IsNullOrEmpty(plaintext))
        {
            plaintext = hasher.Generate();
            var (previousHash, newHash, _) = IngestKeyRotation.Apply(
                app, plaintext, hasher, configuration, timeProvider);
            await appRepo.SaveChangesAsync(ct).ConfigureAwait(false);
            if (previousHash is not null)
                ingestService.InvalidateKeyCache(previousHash);
            ingestService.InvalidateKeyCache(newHash);
            cache.Set(RunKeyCacheKey(app.Id, pipelineRunId), plaintext, RunKeyLifetime);
            await audit.LogAsync(
                previousHash is null ? "CreatedIngestKeyForDeploy" : "RotatedIngestKeyForDeploy",
                "MonitoredApp",
                app.Id,
                $"Version {app.IngestKeyVersion}; pipeline run {pipelineRunId}; previous valid until {app.PreviousIngestKeyValidUntil:O}",
                ct).ConfigureAwait(false);
        }

        // Mask the plaintext in this run's step logs (it will appear in the env dispatch).
        masking.RegisterRuntimeSecret(pipelineRunId, plaintext);

        var endpoint = baseUrl.TrimEnd('/') + "/api/ingest/otlp/v1";
        var result = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Signal-specific endpoints are exact URLs. A common OTLP HTTP endpoint would append
            // /v1/{signal} and duplicate the route already present in Aetheus.
            ["OTEL_EXPORTER_OTLP_METRICS_ENDPOINT"] = endpoint + "/metrics",
            ["OTEL_EXPORTER_OTLP_LOGS_ENDPOINT"] = endpoint + "/logs",
            ["OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"] = endpoint + "/traces",
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
            ["OTEL_EXPORTER_OTLP_HEADERS"] = $"x-aetheus-ingest-key={plaintext}",
            ["AETHEUS_VISITOR_ENDPOINT"] = baseUrl.TrimEnd('/') + "/api/ingest/visitors",
            ["AETHEUS_INGEST_KEY"] = plaintext,
            ["AETHEUS_TELEMETRY_ENABLED"] = "true",
            ["AETHEUS_TELEMETRY_LOGS_ENABLED"] = "true",
            ["AETHEUS_TELEMETRY_APPLICATION_ID"] = app.Id.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ["OTEL_SERVICE_NAME"] = app.Name
        };

        // Web analytics reaches the front through the same compose_env contract as telemetry, and that
        // contract refuses a listed variable the run does not define rather than letting Compose fall
        // back to its own default. The whole set is therefore always emitted: when the feature is not
        // configured the two key values are inert placeholders and AETHEUS_WEB_ANALYTICS_ENABLED stays
        // "false", which stops the browser module before it ever reads them.
        const string AnalyticsDisabledPlaceholder = "disabled";
        var analyticsPseudonymizationKey = AnalyticsDisabledPlaceholder;
        var analyticsIngestKey = AnalyticsDisabledPlaceholder;
        var analyticsEnabled = false;

        if (app.AnalyticsEnabled && app.AnalyticsVaultName is not null)
        {
            var secrets = await vaults.ResolveVaultSecretsAsync(
                [app.AnalyticsVaultName],
                app.ProjectId,
                ct).ConfigureAwait(false);
            if (secrets.TryGetValue(AppWebAnalyticsConfigurationService.SecretKey, out var pseudonymizationKey))
            {
                masking.RegisterRuntimeSecret(pipelineRunId, pseudonymizationKey);
                analyticsPseudonymizationKey = pseudonymizationKey;
                analyticsIngestKey = plaintext;
                analyticsEnabled = true;
            }
        }

        result["AETHEUS_WEB_ANALYTICS_ENABLED"] = analyticsEnabled ? "true" : "false";
        result["AETHEUS_WEB_ANALYTICS_APPLICATION_ID"] = app.Id.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        result["AETHEUS_WEB_ANALYTICS_SITE_ID"] = app.AnalyticsSiteId ?? $"app-{app.Id}";
        result["AETHEUS_WEB_ANALYTICS_INGEST_ENDPOINT"] =
            baseUrl.TrimEnd('/') + "/api/ingest/web-analytics/v1/events";
        result["AETHEUS_WEB_ANALYTICS_INGEST_KEY"] = analyticsIngestKey;
        result["AETHEUS_WEB_ANALYTICS_PSEUDONYMIZATION_KEY"] = analyticsPseudonymizationKey;
        result["AETHEUS_WEB_ANALYTICS_PSEUDONYMIZATION_KEY_VERSION"] =
            app.AnalyticsPseudonymKeyVersion.ToString(
                System.Globalization.CultureInfo.InvariantCulture);

        return result;
    }
}
