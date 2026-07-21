// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Environment = Aetheus.Back.Data.Entities.Environment;

namespace Aetheus.Back.Components.Environments;

public class EnvironmentService(
    IEnvironmentRepository repo,
    IAuditService audit,
    IEntityChangeNotifier notifier,
    IServiceScopeFactory scopeFactory) : IEnvironmentService
{
    public async Task<PaginatedResult<EnvironmentDto>> GetEnvironmentsAsync(int? projectId, PaginationRequest request, List<int>? accessibleIds = null, CancellationToken ct = default)
    {
        var (page, pageSize) = request.Normalize();
        var (items, totalCount) = await repo.GetEnvironmentsPagedAsync(
            request.Search, projectId, page, pageSize, accessibleIds, ct).ConfigureAwait(false);

        return new PaginatedResult<EnvironmentDto>
        {
            Items = items.Select(MapToDto).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<EnvironmentDto?> GetEnvironmentAsync(int id, CancellationToken ct = default)
    {
        var env = await repo.GetEnvironmentWithServersAsync(id, ct).ConfigureAwait(false);
        return env is null ? null : MapToDto(env);
    }

    public async Task<EnvironmentDto> CreateEnvironmentAsync(CreateEnvironmentRequest request, CancellationToken ct = default)
    {
        var env = new Environment
        {
            Name = request.Name,
            Description = request.Description,
            Type = request.Type,
            ProjectId = request.ProjectId,
            RequireApproval = request.RequireApproval,
            ApprovalTimeoutMinutes = request.ApprovalTimeoutMinutes,
            ApprovalInstructions = request.ApprovalInstructions,
            Servers = request.ServerIds.Select(sid => new EnvironmentServer { ServerId = sid }).ToList()
        };

        await repo.AddEnvironmentAsync(env, ct).ConfigureAwait(false);
        await audit.LogAsync("Created", "Environment", env.Id, env.Name, ct).ConfigureAwait(false);
        await notifier.BroadcastAsync(ResourceType.Environment, env.Id, EntityChangeOps.Created, ct).ConfigureAwait(false);

        var result = await repo.GetEnvironmentWithServersAsync(env.Id, ct).ConfigureAwait(false);
        return MapToDto(result!);
    }

    public async Task<EnvironmentDto?> UpdateEnvironmentAsync(int id, UpdateEnvironmentRequest request, CancellationToken ct = default)
    {
        var env = await repo.FindEnvironmentAsync(id, ct).ConfigureAwait(false);
        if (env is null) return null;

        env.Name = request.Name;
        env.Description = request.Description;
        env.Type = request.Type;
        env.ProjectId = request.ProjectId;
        env.RequireApproval = request.RequireApproval;
        env.ApprovalTimeoutMinutes = request.ApprovalTimeoutMinutes;
        env.ApprovalInstructions = request.ApprovalInstructions;
        // UpdatedAt is stamped centrally by AppDbContext.SaveChangesAsync.

        // Replace servers
        env.Servers.Clear();
        env.Servers.AddRange(request.ServerIds.Select(sid => new EnvironmentServer { EnvironmentId = id, ServerId = sid }));

        await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.LogAsync("Updated", "Environment", env.Id, env.Name, ct).ConfigureAwait(false);
        await notifier.BroadcastAsync(ResourceType.Environment, env.Id, EntityChangeOps.Updated, ct).ConfigureAwait(false);

        // F-007: pipeline copy is best-effort and can involve a git clone - dispatched in the
        // background so the HTTP response returns immediately regardless of repo size/count.
        if (env.ProjectId is { } linkedProjectId)
            DispatchPipelineCopyInBackground(env.Id, env.Name, linkedProjectId);

        var result = await repo.GetEnvironmentWithServersAsync(id, ct).ConfigureAwait(false);
        return MapToDto(result!);
    }

    private void DispatchPipelineCopyInBackground(int environmentId, string environmentName, int projectId)
    {
        _ = Task.Run(async () =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var git = scope.ServiceProvider.GetRequiredService<IPipelineGitService>();
            var log = scope.ServiceProvider.GetRequiredService<ILogger<EnvironmentService>>();
            try
            {
                var copied = await git.CopyEnvironmentPipelinesToProjectAsync(
                    environmentId, environmentName, projectId, "system").ConfigureAwait(false);
                if (copied > 0)
                    log.LogInformation("Copied {Count} environment '{Env}' pipeline(s) into project {ProjectId} git.", copied, environmentName, projectId);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Background pipeline copy failed for env '{Env}' → project {ProjectId}.", environmentName, projectId);
            }
        });
    }

    public async Task<bool> DeleteEnvironmentAsync(int id, CancellationToken ct = default)
    {
        var env = await repo.FindEnvironmentAsync(id, ct).ConfigureAwait(false);
        if (env is null) return false;

        var name = env.Name;
        await repo.RemoveEnvironmentAsync(env, ct).ConfigureAwait(false);
        await audit.LogAsync("Deleted", "Environment", id, name, ct).ConfigureAwait(false);
        await notifier.BroadcastAsync(ResourceType.Environment, id, EntityChangeOps.Deleted, ct).ConfigureAwait(false);
        return true;
    }

    public async Task<EnvironmentDto?> DuplicateEnvironmentAsync(int envId, int? targetProjectId, CancellationToken ct = default)
    {
        var source = await repo.GetEnvironmentForDuplicationAsync(envId, ct).ConfigureAwait(false);
        if (source is null) return null;

        var projectId = targetProjectId ?? source.ProjectId;
        // English suffix - backend-generated strings follow the product default language (F-036).
        var baseName = source.Name + " (copy)";
        var name = baseName;
        var attempt = 2;
        while (await repo.NameExistsInProjectAsync(name, projectId, ct).ConfigureAwait(false))
        {
            name = $"{source.Name} ({attempt})";
            attempt++;
        }

        var clone = new Environment
        {
            Name = name,
            Description = source.Description,
            Type = source.Type,
            ProjectId = projectId,
            RequireApproval = source.RequireApproval,
            ApprovalTimeoutMinutes = source.ApprovalTimeoutMinutes,
            ApprovalInstructions = source.ApprovalInstructions,
            Servers = source.Servers.Select(es => new Data.Entities.EnvironmentServer { ServerId = es.ServerId }).ToList(),
            Checks = source.Checks.Select(c => new Data.Entities.EnvironmentCheck
            {
                Name = c.Name,
                Type = c.Type,
                Configuration = c.Configuration,
                IsRequired = c.IsRequired,
                TimeoutSeconds = c.TimeoutSeconds
            }).ToList(),
            LinkedProjectServers = source.LinkedProjectServers
                .Select(lps => new Data.Entities.EnvironmentProjectServer { ProjectServerId = lps.ProjectServerId }).ToList(),
            Libraries = source.Libraries.Select(l => new Data.Entities.VariableLibrary
            {
                Name = l.Name,
                Description = l.Description,
                Entries = l.Entries.Select(e => new Data.Entities.VariableLibraryEntry
                {
                    Key = e.Key,
                    Value = e.Value
                }).ToList()
            }).ToList(),
            Vaults = source.Vaults.Select(v => new Data.Entities.Vault
            {
                Name = v.Name,
                Description = v.Description,
                Secrets = v.Secrets.Select(s => new Data.Entities.VaultSecret
                {
                    Key = s.Key,
                    EncryptedValue = s.EncryptedValue,
                    ExpiresAt = s.ExpiresAt
                }).ToList()
            }).ToList(),
            Pipelines = source.Pipelines.Select(p => new Data.Entities.Pipeline
            {
                Name = p.Name,
                Description = p.Description,
                YamlDefinition = p.YamlDefinition,
                TriggerType = p.TriggerType,
                CreatedByUsername = p.CreatedByUsername
            }).ToList()
        };

        await repo.AddEnvironmentAsync(clone, ct).ConfigureAwait(false);
        await audit.LogAsync("Duplicated", "Environment", clone.Id, $"{clone.Name} (from {source.Name})", ct).ConfigureAwait(false);
        await notifier.BroadcastAsync(ResourceType.Environment, clone.Id, EntityChangeOps.Created, ct).ConfigureAwait(false);

        var result = await repo.GetEnvironmentWithServersAsync(clone.Id, ct).ConfigureAwait(false);
        return MapToDto(result!);
    }

    public Task<int?> GetProjectServerProjectIdAsync(int projectServerId, CancellationToken ct = default)
        => repo.GetProjectServerProjectIdAsync(projectServerId, ct);

    public async Task<bool> LinkProjectServerAsync(int envId, int projectServerId, CancellationToken ct = default)
    {
        var env = await repo.FindEnvironmentAsync(envId, ct).ConfigureAwait(false);
        if (env is null) return false;
        if (await repo.LinkExistsAsync(envId, projectServerId, ct).ConfigureAwait(false)) return true;

        await repo.AddLinkAsync(new Data.Entities.EnvironmentProjectServer
        {
            EnvironmentId = envId,
            ProjectServerId = projectServerId
        }, ct).ConfigureAwait(false);

        await audit.LogAsync("LinkedProjectServer", "Environment", envId, $"PS#{projectServerId}", ct).ConfigureAwait(false);
        await notifier.BroadcastAsync(ResourceType.Environment, envId, EntityChangeOps.Updated, ct).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> UnlinkProjectServerAsync(int envId, int projectServerId, CancellationToken ct = default)
    {
        if (!await repo.LinkExistsAsync(envId, projectServerId, ct).ConfigureAwait(false)) return false;
        await repo.RemoveLinkAsync(envId, projectServerId, ct).ConfigureAwait(false);
        await audit.LogAsync("UnlinkedProjectServer", "Environment", envId, $"PS#{projectServerId}", ct).ConfigureAwait(false);
        await notifier.BroadcastAsync(ResourceType.Environment, envId, EntityChangeOps.Updated, ct).ConfigureAwait(false);
        return true;
    }

    private static EnvironmentDto MapToDto(Environment e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Description = e.Description,
        Type = e.Type,
        ProjectId = e.ProjectId,
        ProjectName = e.Project?.Name,
        RequireApproval = e.RequireApproval,
        ApprovalTimeoutMinutes = e.ApprovalTimeoutMinutes,
        ApprovalInstructions = e.ApprovalInstructions,
        Servers = e.Servers.Select(es => new EnvironmentServerDto
        {
            ServerId = es.ServerId,
            ServerName = es.Server?.Name ?? string.Empty,
            ServerStatus = es.Server?.Status ?? ServerStatus.Offline
        }).ToList(),
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt
    };
}
