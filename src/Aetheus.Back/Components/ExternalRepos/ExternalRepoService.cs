// SPDX-License-Identifier: EUPL-1.2
using System.Text;
using System.Text.Json;
using Aetheus.Back.Components.Git;
using Aetheus.Back.Components.Projects;
using Aetheus.Back.Components.ServiceConnections;
using Aetheus.Back.Configuration;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Aetheus.Back.Components.ExternalRepos;

public sealed class ExternalRepoService(
    IProjectRepository projectRepo,
    IGitRepository gitRepo,
    IGitLightRepository lightRepo,
    IServiceConnectionRepository serviceConnectionRepo,
    IExternalRepoMirrorService mirror,
    IEncryptionService encryption,
    IAuditService audit,
    IEntityChangeNotifier notifier,
    IHttpContextAccessor httpContextAccessor,
    IConfiguration configuration,
    IOptions<FeatureFlagsOptions> features,
    TimeProvider timeProvider,
    ILogger<ExternalRepoService> logger) : IExternalRepoService
{
    public bool IsEnabled => features.Value.ExternalRepos;

    public async Task<ExternalRepoDto?> GetForProjectAsync(int projectId, CancellationToken ct = default)
    {
        var project = await projectRepo.FindProjectAsync(projectId, ct).ConfigureAwait(false);
        if (project?.GitConnectionId is null)
        {
            return null;
        }

        var connection = await gitRepo.FindConnectionAsync(project.GitConnectionId.Value, ct).ConfigureAwait(false);
        if (connection is null)
        {
            return null;
        }

        var mirrorRepo = await FindMirrorAsync(projectId, connection.Id, ct).ConfigureAwait(false);
        return Map(connection, mirrorRepo, project.RepositoryUrl, await ResolveAuthTypeAsync(connection, ct).ConfigureAwait(false));
    }

    public async Task<ExternalRepoDto> AttachAsync(AttachExternalRepoRequest request, CancellationToken ct = default)
    {
        EnsureEnabled();

        var project = await projectRepo.FindProjectAsync(request.ProjectId, ct).ConfigureAwait(false)
            ?? throw new NotFoundException($"Project {request.ProjectId} not found.");

        // XOR source guard: a project is either internal (has an internal repo / RepositoryUrl) or
        // external (GitConnectionId). Reject attaching a second source.
        if (project.GitConnectionId is not null)
        {
            throw new ConflictException("This project already has an external repository attached.");
        }
        var existingInternal = await lightRepo.GetByProjectAsync(request.ProjectId, ct).ConfigureAwait(false);
        if (existingInternal.Count > 0)
        {
            throw new ConflictException("This project already has an internal repository; detach it before attaching an external one.");
        }

        var credential = BuildCredential(request);
        ValidateCredential(credential);

        // Fail fast on an unreachable remote / bad credential before persisting anything.
        var (ok, error) = await mirror.TestConnectivityAsync(
            request.ProviderType, request.BaseUrl, request.OwnerOrGroup, request.RepositoryName,
            request.AuthType, credential, ct).ConfigureAwait(false);
        if (!ok)
        {
            throw new BadRequestException($"Could not reach the external repository: {error}");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        var serviceConnection = new ServiceConnection
        {
            Name = $"git-{request.OwnerOrGroup}-{request.RepositoryName}",
            Description = "External Git credential (managed by External-Git).",
            Type = request.AuthType == GitAuthType.Ssh ? ServiceConnectionType.SSH : ServiceConnectionType.Generic,
            ProjectId = request.ProjectId,
            EncryptedPayload = encryption.EncryptValue(JsonSerializer.Serialize(credential))
        };
        await serviceConnectionRepo.AddAsync(serviceConnection, ct).ConfigureAwait(false);

        var connection = new GitConnection
        {
            ProjectId = request.ProjectId,
            ProviderType = request.ProviderType,
            OwnerOrGroup = request.OwnerOrGroup,
            RepositoryName = request.RepositoryName,
            BaseUrl = request.BaseUrl,
            ServiceConnectionId = serviceConnection.Id,
            AutoSyncEnabled = request.AutoSyncEnabled,
            FetchIntervalMinutes = Math.Clamp(request.FetchIntervalMinutes, 1, 1440),
            WriteEnabled = request.WriteEnabled,
            MirrorStatus = GitMirrorStatus.NotInitialized
        };
        await gitRepo.AddConnectionAsync(connection, ct).ConfigureAwait(false);

        var slug = Slugify(request.RepositoryName);
        var mirrorRepo = new GitInternalRepo
        {
            ProjectId = request.ProjectId,
            Name = request.RepositoryName,
            Slug = slug,
            Description = "External repository mirror.",
            DefaultBranch = string.IsNullOrWhiteSpace(request.DefaultBranch) ? "main" : request.DefaultBranch,
            IsEmpty = true,
            GitConnectionId = connection.Id
        };
        await lightRepo.AddAsync(mirrorRepo, ct).ConfigureAwait(false);

        // Initial mirror clone. On failure the connection is left in Error state but still attached so
        // the user can fix credentials and re-sync - no silent success.
        await mirror.SyncAsync(connection, mirrorRepo, ct).ConfigureAwait(false);
        await gitRepo.SaveChangesAsync(ct).ConfigureAwait(false);
        await lightRepo.SaveChangesAsync(ct).ConfigureAwait(false);

        // Mark the project's source as external and point pipelines/browse at the mirror's smart-HTTP
        // URL. The external credential never leaves the backend; the agent clones the mirror instead.
        project.GitConnectionId = connection.Id;
        project.RepositoryUrl = BuildMirrorCloneUrl(request.ProjectId, slug);
        project.DefaultBranch = mirrorRepo.DefaultBranch;
        project.UpdatedAt = now;
        await projectRepo.SaveChangesAsync(ct).ConfigureAwait(false);

        await audit.LogAsync("Attached", "ExternalRepo", connection.Id,
            $"{request.ProviderType}: {request.OwnerOrGroup}/{request.RepositoryName}", ct).ConfigureAwait(false);
        await notifier.BroadcastAsync(ResourceType.Project, request.ProjectId, EntityChangeOps.Updated, ct).ConfigureAwait(false);

        return Map(connection, mirrorRepo, project.RepositoryUrl, request.AuthType);
    }

    public async Task<ExternalRepoDto?> SyncNowAsync(int projectId, CancellationToken ct = default)
    {
        EnsureEnabled();

        var project = await projectRepo.FindProjectAsync(projectId, ct).ConfigureAwait(false);
        if (project?.GitConnectionId is null)
        {
            return null;
        }

        var connection = await gitRepo.FindConnectionAsync(project.GitConnectionId.Value, ct).ConfigureAwait(false);
        var mirrorRepo = await FindMirrorAsync(projectId, project.GitConnectionId.Value, ct).ConfigureAwait(false);
        if (connection is null || mirrorRepo is null)
        {
            return null;
        }

        await mirror.SyncAsync(connection, mirrorRepo, ct).ConfigureAwait(false);
        await gitRepo.SaveChangesAsync(ct).ConfigureAwait(false);
        await lightRepo.SaveChangesAsync(ct).ConfigureAwait(false);
        await notifier.BroadcastAsync(ResourceType.Project, projectId, EntityChangeOps.Updated, ct).ConfigureAwait(false);

        return Map(connection, mirrorRepo, project.RepositoryUrl, await ResolveAuthTypeAsync(connection, ct).ConfigureAwait(false));
    }

    public async Task<bool> DetachAsync(int projectId, CancellationToken ct = default)
    {
        EnsureEnabled();

        var project = await projectRepo.FindProjectAsync(projectId, ct).ConfigureAwait(false);
        if (project?.GitConnectionId is null)
        {
            return false;
        }

        var connectionId = project.GitConnectionId.Value;
        var connection = await gitRepo.FindConnectionAsync(connectionId, ct).ConfigureAwait(false);
        var mirrorRepo = await FindMirrorAsync(projectId, connectionId, ct).ConfigureAwait(false);

        // Null the back-reference first (NoAction FK) so the connection can be removed.
        project.GitConnectionId = null;
        project.RepositoryUrl = null;
        project.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await projectRepo.SaveChangesAsync(ct).ConfigureAwait(false);

        if (mirrorRepo is not null)
        {
            DeleteMirrorOnDisk(projectId, mirrorRepo.Slug);
            await lightRepo.RemoveAsync(mirrorRepo, ct).ConfigureAwait(false);
            await lightRepo.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        if (connection is not null)
        {
            var serviceConnectionId = connection.ServiceConnectionId;
            await gitRepo.RemoveConnectionAsync(connection, ct).ConfigureAwait(false);
            await gitRepo.SaveChangesAsync(ct).ConfigureAwait(false);

            if (serviceConnectionId is not null)
            {
                var sc = await serviceConnectionRepo.FindAsync(serviceConnectionId.Value, ct).ConfigureAwait(false);
                if (sc is not null)
                {
                    await serviceConnectionRepo.RemoveAsync(sc, ct).ConfigureAwait(false);
                    await serviceConnectionRepo.SaveChangesAsync(ct).ConfigureAwait(false);
                }
            }
        }

        await audit.LogAsync("Detached", "ExternalRepo", connectionId, null, ct).ConfigureAwait(false);
        await notifier.BroadcastAsync(ResourceType.Project, projectId, EntityChangeOps.Updated, ct).ConfigureAwait(false);
        return true;
    }

    private void EnsureEnabled()
    {
        if (!IsEnabled)
        {
            throw new NotFoundException("External repositories are not enabled.");
        }
    }

    private async Task<GitInternalRepo?> FindMirrorAsync(int projectId, int connectionId, CancellationToken ct)
    {
        var repos = await lightRepo.GetByProjectAsync(projectId, ct).ConfigureAwait(false);
        return repos.FirstOrDefault(r => r.GitConnectionId == connectionId);
    }

    private async Task<GitAuthType> ResolveAuthTypeAsync(GitConnection connection, CancellationToken ct)
    {
        if (connection.ServiceConnectionId is null)
        {
            return GitAuthType.HttpsToken;
        }

        var sc = await serviceConnectionRepo.FindAsync(connection.ServiceConnectionId.Value, ct).ConfigureAwait(false);
        return sc?.Type == ServiceConnectionType.SSH ? GitAuthType.Ssh : GitAuthType.HttpsToken;
    }

    private static GitCredentialPayload BuildCredential(AttachExternalRepoRequest request) => new()
    {
        AuthType = request.AuthType,
        Username = request.Username,
        Token = request.Token,
        PrivateKeyPem = request.PrivateKeyPem,
        Passphrase = request.Passphrase,
        KnownHosts = request.KnownHosts
    };

    private static void ValidateCredential(GitCredentialPayload credential)
    {
        if (credential.AuthType == GitAuthType.HttpsToken && string.IsNullOrWhiteSpace(credential.Token))
        {
            throw new BadRequestException("An access token is required for HTTPS authentication.");
        }

        if (credential.AuthType == GitAuthType.Ssh)
        {
            if (string.IsNullOrWhiteSpace(credential.PrivateKeyPem))
            {
                throw new BadRequestException("A private key is required for SSH authentication.");
            }
            if (string.IsNullOrWhiteSpace(credential.KnownHosts))
            {
                // Non-negotiable: we never fall back to StrictHostKeyChecking=no.
                throw new BadRequestException("A pinned known_hosts entry is required for SSH authentication.");
            }
        }
    }

    private string BuildMirrorCloneUrl(int projectId, string slug) =>
        MirrorCloneUrl.Build(configuration, httpContextAccessor.HttpContext, projectId, slug);

    private void DeleteMirrorOnDisk(int projectId, string slug)
    {
        var path = mirror.ResolveMirrorPath(projectId, slug);
        if (Directory.Exists(path))
        {
            GitProcessRunner.ClearReadOnlyAttributes(path);
            Directory.Delete(path, recursive: true);
            logger.LogInformation("[ExternalRepos] removed mirror on disk for project {ProjectId}", projectId);
        }
    }

    private static string Slugify(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
            else if (ch is ' ' or '-' or '_' or '.' or '/')
            {
                sb.Append('-');
            }
        }

        var slug = sb.ToString().Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return string.IsNullOrEmpty(slug) ? "repo" : slug;
    }

    private static ExternalRepoDto Map(GitConnection connection, GitInternalRepo? mirror, string? cloneUrl, GitAuthType authType) => new()
    {
        GitConnectionId = connection.Id,
        ProjectId = connection.ProjectId,
        ProviderType = connection.ProviderType,
        OwnerOrGroup = connection.OwnerOrGroup,
        RepositoryName = connection.RepositoryName,
        BaseUrl = connection.BaseUrl,
        AuthType = authType,
        MirrorStatus = connection.MirrorStatus,
        LastFetchedAt = connection.LastFetchedAt,
        LastFetchError = connection.LastFetchError,
        AutoSyncEnabled = connection.AutoSyncEnabled,
        FetchIntervalMinutes = connection.FetchIntervalMinutes,
        WriteEnabled = connection.WriteEnabled,
        DefaultBranch = mirror?.DefaultBranch,
        CloneUrl = cloneUrl
    };
}
