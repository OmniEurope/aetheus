// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using System.Text.RegularExpressions;
using Aetheus.Back.Components.ExternalRepos;
using Aetheus.Back.Components.Settings;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Services;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Components.Pipelines;

public partial class PipelineWebhookService(
    IPipelineRepository pipelineRepo,
    IPipelineRunService runService,
    ISettingsService settingsService,
    ILogger<PipelineWebhookService> logger,
    IPipelineGitService? pipelineGitService = null) : IPipelineWebhookService
{
    // WEBHOOK_REF / WEBHOOK_REPO become $(VAR)-substitutable text in step shell scripts
    // (PipelineCommandBuilder.Substitute splices values verbatim), so anyone holding the
    // shared WebhookSecret could inject shell through a hostile "ref". Constrain both to
    // their legitimate shapes at the trust boundary: git refs and https repo URLs never
    // need shell metacharacters.
    // \A...\z (not ^...$): in .NET `$` also matches just BEFORE a trailing newline, so `^...$`
    // would accept a ref/URL ending in "\n" and let that newline (a shell metacharacter) through
    // into PipelineCommandBuilder.Substitute. \z anchors the true end of string.
    // First char excludes '-': a leading dash would be an argument-injection token if an author's
    // step does `git checkout $(WEBHOOK_REF)` (e.g. `-oProxyCommand`); a real git ref / URL never
    // starts with '-', matching the certbot-email validation discipline.
    [GeneratedRegex(@"\A[A-Za-z0-9._/@+][A-Za-z0-9._/@+-]{0,299}\z")]
    private static partial Regex SafeGitRefRegex();

    [GeneratedRegex(@"\A[A-Za-z0-9.:/_@~+][A-Za-z0-9.:/_@~+-]{0,499}\z")]
    private static partial Regex SafeRepoUrlRegex();

    public async Task<bool> HandleWebhookAsync(string rawBody, string? signature, CancellationToken ct = default)
    {
        // F-07: webhook secret is mandatory; refuse fail-open when not configured.
        var secret = await settingsService.GetSettingValueAsync("WebhookSecret", ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(secret))
        {
            logger.LogWarning("Webhook rejected: WebhookSecret is not configured.");
            return false;
        }

        if (!WebhookSignatureValidator.Validate(signature, secret, rawBody))
        {
            logger.LogWarning("Invalid webhook signature");
            return false;
        }

        // Parse the payload to extract repo URL and branch
        var payload = ParsePayload(rawBody);
        if (payload is null)
        {
            logger.LogWarning("Could not parse webhook payload");
            return false;
        }

        if (!SafeRepoUrlRegex().IsMatch(payload.RepositoryUrl)
            || (payload.Ref.Length > 0 && !SafeGitRefRegex().IsMatch(payload.Ref)))
        {
            logger.LogWarning("Webhook rejected: repository URL or ref contains characters outside the allowed git shapes.");
            return false;
        }

        // Find pipelines with webhook trigger matching this repository
        var webhookPipelines = await pipelineRepo.GetWebhookTriggeredPipelinesWithProjectAsync(ct).ConfigureAwait(false);

        var triggered = false;

        foreach (var pipeline in webhookPipelines)
        {
            // Match the incoming repo URL against the project's internal repo URL OR, for an
            // External-Git project, against its reconstructed external remote (RepositoryUrl then
            // holds the internal mirror URL, which never equals the external webhook URL).
            if (!MatchesRepository(pipeline, payload.RepositoryUrl)) continue;

            // Optional branch filter: a pipeline whose YAML declares `branches:` only fires when the
            // pushed ref matches one of the patterns. No filter => any branch (backward-compatible).
            var authoritativeYaml = pipeline.YamlDefinition;
            if (pipelineGitService is not null && pipeline.ProjectId is { } projectId)
            {
                var gitYaml = await pipelineGitService.ReadProjectPipelineYamlAsync(
                        projectId, pipeline.Name, ct, pipeline.SourceBranch)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(gitYaml))
                    authoritativeYaml = gitYaml;
            }

            var definition = YamlParsingHelper.ParseAndValidate(authoritativeYaml, logger);
            if (definition is null && PipelineBranchFilter.DeclaresFilter(authoritativeYaml))
            {
                // Fail-closed: the stored YAML declares a branches: filter but can no longer be parsed
                // to recover the patterns. Falling back to an empty filter here would match on EVERY
                // branch - the opposite of the author's declared intent - so skip instead.
                logger.LogWarning(
                    "Webhook skipped pipeline {PipelineId}: YAML declares a branches filter but failed to parse; failing closed instead of matching every branch",
                    pipeline.Id);
                continue;
            }

            var branches = definition?.Branches ?? [];
            if (!PipelineBranchFilter.Matches(branches, payload.Ref, logger))
            {
                logger.LogDebug("Webhook skipped pipeline {PipelineId}: ref '{Ref}' does not match its branch filter",
                    pipeline.Id, payload.Ref);
                continue;
            }

            // Check if there's already a running run
            var hasActiveRun = await pipelineRepo.HasActiveRunAsync(pipeline.Id, ct).ConfigureAwait(false);

            if (hasActiveRun) continue;

            logger.LogInformation("Webhook triggering pipeline {PipelineId} ({PipelineName})",
                pipeline.Id, pipeline.Name);

            var variables = new Dictionary<string, string>
            {
                ["WEBHOOK_REF"] = payload.Ref,
                ["WEBHOOK_REPO"] = payload.RepositoryUrl
            };

            // F-EXEC-1b: no caller principal - TriggerAutomatedRunAsync authorizes the pipeline
            // owner and fail-closes (returns null) if the pipeline is unowned or the owner lacks
            // Server.Admin on a target.
            await runService.TriggerAutomatedRunAsync(pipeline.Id, "Webhook", variables, ct).ConfigureAwait(false);
            triggered = true;
        }

        return triggered || !string.IsNullOrEmpty(secret);
    }

    // External-Git webhooks: a mirror-backed project's RepositoryUrl is the internal mirror URL, so
    // the incoming external repo URL is matched against the remote reconstructed from its GitConnection.
    private static bool MatchesRepository(Pipeline pipeline, string incomingRepoUrl)
    {
        var project = pipeline.Project;
        if (project is null) return false;

        var incoming = RepositoryUrlNormalizer.Normalize(incomingRepoUrl);

        if (project.RepositoryUrl is not null
            && RepositoryUrlNormalizer.Normalize(project.RepositoryUrl).Equals(incoming, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var connection = project.GitConnection;
        // A self-hosted/AetheusGit connection without a BaseUrl has no resolvable host, so
        // ExternalRepoUrlBuilder.Build would throw - skip it (it can't match the webhook anyway)
        // rather than let an exception abort the whole webhook dispatch.
        var hasResolvableHost = connection is not null
            && (connection.ProviderType is GitProviderType.GitHub or GitProviderType.GitLab
                || !string.IsNullOrWhiteSpace(connection.BaseUrl));
        if (connection is not null && hasResolvableHost)
        {
            // HttpsToken yields the https remote form; webhook payloads always carry the https
            // html_url / web_url, so normalized equality matches regardless of the connection's auth.
            var externalUrl = ExternalRepoUrlBuilder.Build(
                connection.ProviderType, connection.BaseUrl, connection.OwnerOrGroup,
                connection.RepositoryName, GitAuthType.HttpsToken);
            if (RepositoryUrlNormalizer.Normalize(externalUrl).Equals(incoming, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static WebhookInfo? ParsePayload(string rawBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;

            // GitHub format
            if (root.TryGetProperty("repository", out var repo))
            {
                var repoUrl = repo.TryGetProperty("html_url", out var url) ? url.GetString() ?? ""
                            : repo.TryGetProperty("clone_url", out var clone) ? clone.GetString() ?? "" : "";
                var refVal = root.TryGetProperty("ref", out var r) ? r.GetString() ?? "" : "";
                if (!string.IsNullOrEmpty(repoUrl))
                    return new WebhookInfo(repoUrl, refVal);
            }

            // GitLab format
            if (root.TryGetProperty("project", out var project))
            {
                var repoUrl = project.TryGetProperty("web_url", out var url) ? url.GetString() ?? ""
                            : project.TryGetProperty("git_http_url", out var git) ? git.GetString() ?? "" : "";
                var refVal = root.TryGetProperty("ref", out var r) ? r.GetString() ?? "" : "";
                if (!string.IsNullOrEmpty(repoUrl))
                    return new WebhookInfo(repoUrl, refVal);
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record WebhookInfo(string RepositoryUrl, string Ref);
}
