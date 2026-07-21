// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using System.Text;
using Aetheus.Back.Components.Auth;
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Components.Webhooks;
using Aetheus.Back.Configuration;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Hubs;
using Aetheus.Back.Services;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Aetheus.Back.Components.Git;

public class GitSmartHttpService(
    IGitLightRepository lightRepo,
    IAuthService authService,
    IWebhookService webhookService,
    IPipelineService pipelineService,
    IPipelineRunService pipelineRunService,
    IPipelineRepository pipelineRepo,
    IGitLightCliService cli,
    IHubContext<GitRealtimeHub> hub,
    IOptions<GitLightOptions> options,
    ILogger<GitSmartHttpService> logger,
    IMemoryCache cache,
    TimeProvider timeProvider,
    IHostApplicationLifetime applicationLifetime) : IGitSmartHttpService
{
    private static readonly HashSet<string> AllowedServices = ["git-upload-pack", "git-receive-pack"];
    private const string ReachableShaUploadPackConfig = "uploadpack.allowReachableSHA1InWant=true";
    private readonly GitLightOptions _options = options.Value;

    public async Task<(string ContentType, byte[] Body)?> GetInfoRefsAsync(
        int projectId, string slug, string service, CancellationToken ct = default)
    {
        if (!AllowedServices.Contains(service)) return null;

        var entity = await lightRepo.FindBySlugAsync(projectId, slug, ct).ConfigureAwait(false);
        if (entity is null) return null;

        var diskPath = ResolveSafeRepositoryPath(projectId, slug);
        if (diskPath is null) return null;

        // Hardening (High #13): use ArgumentList to avoid any string concatenation that could
        // be interpreted as additional options or quoted-path traversal.
        // Modern git refuses 'git git-upload-pack ...'; use the builtin subcommand name
        // ("upload-pack" / "receive-pack") which works on every supported git version.
        var psi = CreateServiceProcessStartInfo(service, diskPath, advertiseRefs: true);

        using var process = Process.Start(psi);
        if (process is null) return null;

        using var ms = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(ms, ct).ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var output = ms.ToArray();

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
            logger.LogWarning("git {Service} info/refs failed: {Error}", service, error);
            return null;
        }

        // Smart HTTP protocol: pkt-line header + advertised refs
        var contentType = $"application/x-{service}-advertisement";
        var header = $"# service={service}\n";
        var pktHeader = PktLine(header);
        var flush = "0000"u8.ToArray();

        var body = new byte[pktHeader.Length + flush.Length + output.Length];
        pktHeader.CopyTo(body, 0);
        flush.CopyTo(body, pktHeader.Length);
        output.CopyTo(body, pktHeader.Length + flush.Length);

        return (contentType, body);
    }

    public async Task<GitSmartHttpResponse?> ExecuteServiceAsync(
        int projectId, string slug, string service, Stream requestBody, CancellationToken ct = default)
    {
        if (!AllowedServices.Contains(service)) return null;

        var entity = await lightRepo.FindBySlugAsync(projectId, slug, ct).ConfigureAwait(false);
        if (entity is null) return null;

        var diskPath = ResolveSafeRepositoryPath(projectId, slug);
        if (diskPath is null) return null;

        var psi = CreateServiceProcessStartInfo(service, diskPath, advertiseRefs: false);

        var isUploadPack = string.Equals(service, "git-upload-pack", StringComparison.Ordinal);

        var process = Process.Start(psi);
        if (process is null) return null;

        // F-30 + Hardening (High #14): hard upper bound + kill on timeout/cancellation.
        // NOT a `using` - for the streamed upload-pack path ownership of the CTS transfers to the
        // returned GitProcessOutputStream (disposed when the response finishes). The buffered and
        // error/cancel paths dispose it explicitly below.
        var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(BackendRuntimeDefaults.GitLongRunningTimeout);
        var token = timeoutCts.Token;

        try
        {
            IReadOnlyList<GitRefUpdate> requestedUpdates = isUploadPack
                ? []
                : await GitReceivePackRefParser.CopyAndExtractUpdatedRefsAsync(
                    requestBody, process.StandardInput.BaseStream, token).ConfigureAwait(false);
            if (isUploadPack)
                await requestBody.CopyToAsync(process.StandardInput.BaseStream, token).ConfigureAwait(false);
            await process.StandardInput.BaseStream.FlushAsync(token).ConfigureAwait(false);
            process.StandardInput.Close();

            // S-TECH-GP9S: upload-pack is read-only (clone/fetch) with no post-step, so stream its
            // output straight to the HTTP response instead of buffering the whole packfile in memory.
            // The returned stream owns the process + CTS. See ADR-020 for the changed error semantics:
            // a git failure now surfaces as a truncated pkt-line stream (the client detects it), not a
            // 404, because the 200 is already committed once bytes start flowing.
            if (isUploadPack)
            {
                var uploadContentType = $"application/x-{service}-result";
                return new GitSmartHttpResponse(uploadContentType,
                    new GitProcessOutputStream(process, timeoutCts, logger, service), []);
            }

            // PERF NOTE (assessed 2026-06-21): receive-pack output stays buffered into a MemoryStream -
            // STRUCTURALLY REQUIRED, not an oversight:
            //   1. receive-pack ordering - the controller calls MarkPushedAsync() AFTER this method
            //      returns. Streaming would return before git finished applying the push, so the post
            //      step (HEAD fixup, webhooks, pipeline triggers) would run against a stale repo.
            //   2. error semantics - a non-zero git exit maps to NotFound (404). That decision needs
            //      the exit code, only known once the process has fully run; a streamed 200 cannot be
            //      retracted once bytes start flowing.
            var ms = new MemoryStream();
            await process.StandardOutput.BaseStream.CopyToAsync(ms, token).ConfigureAwait(false);
            await process.WaitForExitAsync(token).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync(token).ConfigureAwait(false);
                logger.LogWarning("git {Service} failed with exit code {ExitCode}: {Error}", service, process.ExitCode, error);
                process.Dispose();
                timeoutCts.Dispose();
                return null;
            }

            var acceptedUpdates = GitReceivePackStatusParser.GetAcceptedUpdates(ms, requestedUpdates);
            ms.Position = 0;
            var contentType = $"application/x-{service}-result";
            process.Dispose();
            timeoutCts.Dispose();
            return new GitSmartHttpResponse(contentType, ms, acceptedUpdates);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* already exited */ }
            process.Dispose();
            timeoutCts.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Resolves the on-disk repo path for the given project/slug and verifies it stays under
    /// the configured <see cref="GitLightOptions.RepositoriesPath"/> root. Returns <c>null</c>
    /// when the resolved path escapes the root (defense against path-traversal slugs).
    /// </summary>
    private string? ResolveSafeRepositoryPath(int projectId, string slug)
    {
        var candidate = GitRepoPathResolver.TryResolve(_options.RepositoriesPath, projectId, slug);
        if (candidate is null)
            logger.LogWarning("Rejected git path outside repository root");
        return candidate;
    }

    internal static ProcessStartInfo CreateServiceProcessStartInfo(
        string service, string diskPath, bool advertiseRefs)
    {
        var subcommand = service.StartsWith("git-", StringComparison.Ordinal)
            ? service["git-".Length..]
            : service;
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            RedirectStandardInput = !advertiseRefs,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (string.Equals(subcommand, "upload-pack", StringComparison.Ordinal))
        {
            // Long-running pipelines keep their source commit pinned while the advertised branch
            // can advance. Permit only pinned commits that remain reachable from an advertised ref;
            // unlike allowAnySHA1InWant, this does not expose arbitrary unreachable repository data.
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(ReachableShaUploadPackConfig);
        }

        psi.ArgumentList.Add(subcommand);
        psi.ArgumentList.Add("--stateless-rpc");
        if (advertiseRefs)
            psi.ArgumentList.Add("--advertise-refs");
        psi.ArgumentList.Add(diskPath);
        return psi;
    }

    public async Task MarkPushedAsync(int projectId, string slug, IReadOnlyList<GitRefUpdate> updatedRefs, CancellationToken ct = default)
    {
        // receive-pack has already accepted and persisted the refs before this callback starts. The
        // client is therefore free to close its HTTP request while these durable post-receive effects
        // are still running. Tying them to RequestAborted can leave a visible pipeline run persisted as
        // Running with every step Pending when cancellation lands between run creation and dispatch.
        // Keep the work inline so scoped services remain valid, but detach it from the client lifetime.
        var postReceiveCt = applicationLifetime.ApplicationStopping;

        var entity = await lightRepo.FindBySlugAsync(projectId, slug, postReceiveCt).ConfigureAwait(false);
        if (entity is null) return;

        entity.IsEmpty = false;
        entity.LastPushAt = timeProvider.GetUtcNow().UtcDateTime;
        entity.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        // Fix HEAD after push: if HEAD points to a branch with no commits (e.g. bare repo
        // created with --initial-branch main but user pushed to master), re-point HEAD
        // to the actual default branch so that HEAD-based operations (pipeline sync,
        // git log, clone with DEFAULT_BRANCH) work correctly.
        var diskPath = ResolveSafeRepositoryPath(projectId, slug);
        if (diskPath is not null)
        {
            var detected = await cli.DetectDefaultBranchAsync(diskPath, postReceiveCt).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(detected) && detected != entity.DefaultBranch)
            {
                await cli.SetHeadAsync(diskPath, detected, postReceiveCt).ConfigureAwait(false);
                entity.DefaultBranch = detected;
                logger.LogInformation("Updated HEAD of {Slug} to {Branch}", slug, detected);
            }
        }

        await lightRepo.SaveChangesAsync(postReceiveCt).ConfigureAwait(false);

        // G7LM: a push freshens the project's "last git update"; drop the cached value so the project
        // list/detail tiles don't show a stale date for up to the 10-min TTL.
        cache.Remove(ProjectCacheKeys.GitUpdate(projectId));

        // Realtime: notify any client viewing this repo to refresh branches/commits.
        try
        {
            var group = hub.Clients.Group(GitRealtimeGroups.Repository(entity.Id));
            await group.SendAsync(GitRealtimeEvents.RepositoryChanged, entity.Id, postReceiveCt).ConfigureAwait(false);
            await group.SendAsync(GitRealtimeEvents.BranchesChanged, entity.Id, postReceiveCt).ConfigureAwait(false);
            await group.SendAsync(GitRealtimeEvents.CommitsChanged, entity.Id, postReceiveCt).ConfigureAwait(false);
            await group.SendAsync(GitRealtimeEvents.TagsChanged, entity.Id, postReceiveCt).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to broadcast git realtime push event for {Slug}", slug);
        }

        // Fire webhooks inline within the request scope. WebhookService uses a named
        // HttpClient with its own timeout, so this stays bounded and avoids the
        // scoped-service capture that a Task.Run would introduce.
        try
        {
            await webhookService.FireEventAsync("git.push", new
            {
                projectId,
                slug,
                repository = entity.Name,
                pushedAt = entity.LastPushAt
            }, postReceiveCt).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Failed to fire git.push webhook for {Slug}", slug);
        }
        catch (TaskCanceledException ex) when (!postReceiveCt.IsCancellationRequested)
        {
            logger.LogWarning(ex, "git.push webhook timed out for {Slug}", slug);
        }

        // Sync first: a push that changes trigger/branch declarations must affect the selection
        // immediately, rather than only on the following push.
        foreach (var update in updatedRefs)
            await SyncPipelinesFromRepoAsync(entity, update, postReceiveCt).ConfigureAwait(false);

        // Trigger webhook-type pipelines for the branch refs that this receive-pack actually updated.
        try
        {
            var webhookPipelines = await pipelineService.GetWebhookTriggeredPipelinesForProjectAsync(projectId, postReceiveCt).ConfigureAwait(false);
            foreach (var pipeline in webhookPipelines)
            {
                if (await pipelineService.HasActiveRunAsync(pipeline.Id, postReceiveCt).ConfigureAwait(false)) continue;

                var definition = YamlParsingHelper.ParseAndValidate(pipeline.YamlDefinition, logger);
                if (definition is null || !updatedRefs.Any(update => PipelineBranchFilter.Matches(definition.Branches, update.Reference, logger)))
                    continue;

                var matchingUpdate = updatedRefs.First(update =>
                    PipelineBranchFilter.Matches(definition.Branches, update.Reference, logger));

                logger.LogInformation("Git push triggering pipeline {PipelineId} ({PipelineName})", pipeline.Id, pipeline.Name);
                await pipelineRunService.TriggerAutomatedRunAsync(pipeline.Id, "GitPush",
                    new Dictionary<string, string>
                    {
                        ["WEBHOOK_REF"] = matchingUpdate.Reference,
                        ["AETHEUS_SOURCE_COMMIT"] = matchingUpdate.NewObjectId
                    }, postReceiveCt).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to trigger pipelines on push for {Slug}", slug);
        }

    }

    private async Task SyncPipelinesFromRepoAsync(
        Data.Entities.GitInternalRepo gitRepo, GitRefUpdate update, CancellationToken ct)
    {
        try
        {
            var diskPath = ResolveSafeRepositoryPath(gitRepo.ProjectId, gitRepo.Slug);
            if (diskPath is null || gitRepo.ProjectId == 0) return;

            var sourceBranch = update.Reference["refs/heads/".Length..];
            var tree = await cli.GetTreeAsync(diskPath, update.NewObjectId, ".pipeline", ct).ConfigureAwait(false);
            var yamlFiles = tree.Where(e => e.Name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
                                          || e.Name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)).ToList();
            if (yamlFiles.Count == 0) return;

            foreach (var file in yamlFiles)
            {
                var blob = await cli.GetBlobAsync(
                    diskPath, update.NewObjectId, $".pipeline/{file.Name}", ct).ConfigureAwait(false);
                if (blob is null || string.IsNullOrWhiteSpace(blob.Content)) continue;

                var definition = YamlParsingHelper.ParseAndValidate(blob.Content, logger);
                if (definition is null)
                {
                    logger.LogWarning("Invalid pipeline YAML in .pipeline/{File} for repo {Repo}", file.Name, gitRepo.Name);
                    continue;
                }

                var pipelineName = definition.Name;
                if (string.IsNullOrWhiteSpace(pipelineName))
                    pipelineName = Path.GetFileNameWithoutExtension(file.Name);

                await pipelineService.UpsertPipelineFromYamlAsync(
                    pipelineName, blob.Content, gitRepo.ProjectId, definition.Trigger, ct,
                    sourceBranch, gitRepo.DefaultBranch).ConfigureAwait(false);
                logger.LogInformation(
                    "Synced pipeline '{Name}' from {Commit}:.pipeline/{File}",
                    pipelineName, update.NewObjectId, file.Name);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to sync pipelines from .pipeline/ for repo {Repo}", gitRepo.Name);
        }
    }

    public Task<bool> ValidateBasicAuthAsync(string username, string password, CancellationToken ct = default)
        => authService.ValidateBasicAuthAsync(username, password, ct);

    public async Task<bool> IsRunActiveAsync(int runId, CancellationToken ct = default)
    {
        var run = await pipelineRepo.GetPipelineRunWithPipelineAsync(runId, ct).ConfigureAwait(false);
        return run?.Status == PipelineStatus.Running;
    }

    private static byte[] PktLine(string data)
    {
        var bytes = Encoding.UTF8.GetBytes(data);
        var length = bytes.Length + 4;
        var header = Encoding.ASCII.GetBytes(length.ToString("x4"));
        var result = new byte[header.Length + bytes.Length];
        header.CopyTo(result, 0);
        bytes.CopyTo(result, header.Length);
        return result;
    }
}
