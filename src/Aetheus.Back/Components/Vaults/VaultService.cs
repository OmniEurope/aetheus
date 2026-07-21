// SPDX-License-Identifier: EUPL-1.2
using System.Collections.Concurrent;
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Configuration;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Aetheus.Back.Components.Vaults;

public class VaultService(IVaultRepository repo, IEncryptionService encryption, IDbTransactionScope transaction, IAuditService audit, IEntityChangeNotifier notifier, TimeProvider timeProvider, IMemoryCache cache) : IVaultService
{
    private const string VaultNamesCachePrefix = "vaults:names:";
    private static readonly TimeSpan CacheDuration = BackendRuntimeDefaults.ReferenceDataCacheDuration;
    private readonly ConcurrentDictionary<string, byte> _activeNameKeys = new();
    public async Task<PaginatedResult<VaultDto>> GetVaultsAsync(int? projectId, int? environmentId = null, int? projectServerId = null, PaginationRequest? request = null, List<int>? accessibleIds = null, CancellationToken ct = default)
    {
        request ??= new PaginationRequest();
        var (page, pageSize) = request.Normalize();
        var (items, totalCount) = await repo.GetVaultsPagedAsync(
            request.Search, projectId, environmentId, projectServerId, page, pageSize, accessibleIds, ct).ConfigureAwait(false);

        return new PaginatedResult<VaultDto>
        {
            Items = items.Select(MapToDto).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<VaultDetailDto?> GetVaultDetailAsync(int id, CancellationToken ct = default)
    {
        var vault = await repo.GetVaultDetailAsync(id, ct).ConfigureAwait(false);
        if (vault is null) return null;

        return new VaultDetailDto
        {
            Id = vault.Id,
            Name = vault.Name,
            Description = vault.Description,
            ProjectId = vault.ProjectId,
            ProjectName = vault.Project?.Name,
            EnvironmentId = vault.EnvironmentId,
            EnvironmentName = vault.Environment?.Name,
            ProjectServerId = vault.ProjectServerId,
            ProjectServerName = vault.ProjectServer?.DisplayName,
            SecretCount = vault.Secrets.Count,
            CreatedAt = vault.CreatedAt,
            UpdatedAt = vault.UpdatedAt,
            RowVersion = vault.RowVersion,
            Secrets = vault.Secrets.Select(s => new VaultSecretDto
            {
                Id = s.Id,
                Key = s.Key,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt,
                ExpiresAt = s.ExpiresAt,
                VersionCount = s.Versions.Count
            }).ToList()
        };
    }

    public async Task<List<string>> GetVaultNamesAsync(int? projectId, List<int>? accessibleIds = null, CancellationToken ct = default)
    {
        var key = BuildNamesCacheKey(projectId, accessibleIds);
        return await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            _activeNameKeys.TryAdd(key, 0);
            entry.RegisterPostEvictionCallback((k, _, _, _) => _activeNameKeys.TryRemove((string)k, out _));
            return await repo.GetVaultNamesAsync(projectId, accessibleIds, ct).ConfigureAwait(false);
        }).ConfigureAwait(false) ?? [];
    }

    public async Task<VaultDto> CreateVaultAsync(CreateVaultRequest request, CancellationToken ct = default)
    {
        var vault = new Vault
        {
            Name = request.Name,
            Description = request.Description,
            ProjectId = request.ProjectId,
            EnvironmentId = request.EnvironmentId,
            ProjectServerId = request.ProjectServerId
        };

        await repo.AddVaultAsync(vault, ct).ConfigureAwait(false);
        InvalidateNamesCache();
        await audit.LogAsync("Created", "Vault", vault.Id, vault.Name, ct).ConfigureAwait(false);
        await notifier.BroadcastAsync(ResourceType.Vault, vault.Id, EntityChangeOps.Created, ct).ConfigureAwait(false);
        return MapToDto(vault);
    }

    public async Task<VaultDto?> UpdateVaultAsync(int id, UpdateVaultRequest request, CancellationToken ct = default)
    {
        var vault = await repo.GetVaultDetailAsync(id, ct).ConfigureAwait(false);
        if (vault is null) return null;

        if (request.RowVersion != vault.RowVersion)
            throw new ConflictException("The vault was modified by another user. Please reload and try again.");

        vault.Name = request.Name;
        vault.Description = request.Description;
        vault.ProjectId = request.ProjectId;
        vault.EnvironmentId = request.EnvironmentId;
        vault.ProjectServerId = request.ProjectServerId;
        vault.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        vault.RowVersion = Guid.NewGuid();

        try
        {
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("The vault was modified by another user. Please reload and try again.");
        }

        InvalidateNamesCache();
        await audit.LogAsync("Updated", "Vault", vault.Id, vault.Name, ct).ConfigureAwait(false);
        await notifier.BroadcastAsync(ResourceType.Vault, vault.Id, EntityChangeOps.Updated, ct).ConfigureAwait(false);
        return MapToDto(vault);
    }

    public async Task<bool> DeleteVaultAsync(int id, CancellationToken ct = default)
    {
        var vault = await repo.FindVaultAsync(id, ct).ConfigureAwait(false);
        if (vault is null) return false;

        await repo.RemoveVaultAsync(vault, ct).ConfigureAwait(false);
        InvalidateNamesCache();
        await audit.LogAsync("Deleted", "Vault", id, vault.Name, ct).ConfigureAwait(false);
        await notifier.BroadcastAsync(ResourceType.Vault, id, EntityChangeOps.Deleted, ct).ConfigureAwait(false);
        return true;
    }

    public async Task<VaultSecretDto> CreateSecretAsync(int vaultId, CreateVaultSecretRequest request, CancellationToken ct = default)
    {
        var vault = await repo.FindVaultAsync(vaultId, ct).ConfigureAwait(false);
        if (vault is null) throw new NotFoundException("Vault not found");

        var secret = new VaultSecret
        {
            VaultId = vaultId,
            Key = request.Key,
            EncryptedValue = encryption.EncryptValue(request.Value),
            ExpiresAt = request.ExpiresAt
        };

        await transaction.ExecuteInTransactionAsync(async () =>
        {
            await repo.AddSecretAsync(secret, ct).ConfigureAwait(false);

            var version = await repo.GetNextVersionAsync(secret.Id, ct).ConfigureAwait(false);
            await repo.AddSecretVersionAsync(new VaultSecretVersion
            {
                VaultSecretId = secret.Id,
                Key = secret.Key,
                EncryptedValue = secret.EncryptedValue,
                Version = version,
                ChangeType = ChangeType.Created
            }, ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);

        await audit.LogAsync("CreatedSecret", "VaultSecret", secret.Id, secret.Key, ct).ConfigureAwait(false);

        return new VaultSecretDto { Id = secret.Id, Key = secret.Key, CreatedAt = secret.CreatedAt, UpdatedAt = secret.UpdatedAt, ExpiresAt = secret.ExpiresAt };
    }

    public async Task<VaultSecretDto?> UpdateSecretAsync(int vaultId, int secretId, UpdateVaultSecretRequest request, CancellationToken ct = default)
    {
        var secret = await repo.FindSecretAsync(secretId, ct).ConfigureAwait(false);
        if (secret is null || secret.VaultId != vaultId) return null;

        secret.Key = request.Key;
        secret.EncryptedValue = encryption.EncryptValue(request.Value);
        secret.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        secret.ExpiresAt = request.ExpiresAt;

        await transaction.ExecuteInTransactionAsync(async () =>
        {
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);

            var version = await repo.GetNextVersionAsync(secret.Id, ct).ConfigureAwait(false);
            await repo.AddSecretVersionAsync(new VaultSecretVersion
            {
                VaultSecretId = secret.Id,
                Key = secret.Key,
                EncryptedValue = secret.EncryptedValue,
                Version = version,
                ChangeType = ChangeType.Updated
            }, ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);

        await audit.LogAsync("UpdatedSecret", "VaultSecret", secret.Id, secret.Key, ct).ConfigureAwait(false);

        return new VaultSecretDto { Id = secret.Id, Key = secret.Key, CreatedAt = secret.CreatedAt, UpdatedAt = secret.UpdatedAt, ExpiresAt = secret.ExpiresAt };
    }

    public async Task<VaultSecretDto?> RotateSecretAsync(int vaultId, int secretId, RotateVaultSecretRequest request, CancellationToken ct = default)
    {
        var secret = await repo.FindSecretAsync(secretId, ct).ConfigureAwait(false);
        if (secret is null || secret.VaultId != vaultId) return null;

        // Hardening (#48): rotation must produce a different value, otherwise it would create a
        // spurious "Rotated" version with no actual change. Decrypt the current value and bail
        // out early if the caller submitted the same secret material.
        var currentValue = encryption.DecryptValue(secret.EncryptedValue);
        if (string.Equals(currentValue, request.Value, StringComparison.Ordinal))
            throw new BadRequestException("Rotation requires a new value.");

        secret.EncryptedValue = encryption.EncryptValue(request.Value);
        secret.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        secret.ExpiresAt = request.ExpiresAt;

        await transaction.ExecuteInTransactionAsync(async () =>
        {
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);

            var version = await repo.GetNextVersionAsync(secret.Id, ct).ConfigureAwait(false);
            await repo.AddSecretVersionAsync(new VaultSecretVersion
            {
                VaultSecretId = secret.Id,
                Key = secret.Key,
                EncryptedValue = secret.EncryptedValue,
                Version = version,
                ChangeType = ChangeType.Rotated
            }, ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);

        await audit.LogAsync("Rotated", "VaultSecret", secret.Id, secret.Key, ct).ConfigureAwait(false);

        return new VaultSecretDto { Id = secret.Id, Key = secret.Key, CreatedAt = secret.CreatedAt, UpdatedAt = secret.UpdatedAt, ExpiresAt = secret.ExpiresAt };
    }

    public async Task<bool> DeleteSecretAsync(int vaultId, int secretId, CancellationToken ct = default)
    {
        var secret = await repo.FindSecretAsync(secretId, ct).ConfigureAwait(false);
        if (secret is null || secret.VaultId != vaultId) return false;

        await transaction.ExecuteInTransactionAsync(async () =>
        {
            var version = await repo.GetNextVersionAsync(secret.Id, ct).ConfigureAwait(false);
            await repo.AddSecretVersionAsync(new VaultSecretVersion
            {
                VaultSecretId = secret.Id,
                Key = secret.Key,
                EncryptedValue = secret.EncryptedValue,
                Version = version,
                ChangeType = ChangeType.Deleted
            }, ct).ConfigureAwait(false);

            await repo.RemoveSecretAsync(secret, ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);

        await audit.LogAsync("DeletedSecret", "VaultSecret", secret.Id, secret.Key, ct).ConfigureAwait(false);

        return true;
    }

    public async Task<List<VaultSecretVersionDto>> GetSecretVersionsAsync(int vaultId, int secretId, CancellationToken ct = default)
    {
        var secret = await repo.FindSecretAsync(secretId, ct).ConfigureAwait(false);
        if (secret is null || secret.VaultId != vaultId)
            throw new NotFoundException("Secret not found");

        var versions = await repo.GetSecretVersionsAsync(secretId, ct).ConfigureAwait(false);
        return versions.Select(v => new VaultSecretVersionDto
        {
            Version = v.Version,
            Key = v.Key,
            ChangedAt = v.ChangedAt,
            ChangeType = v.ChangeType
        }).ToList();
    }

    public async Task<Dictionary<string, string>> ResolveVaultSecretsAsync(List<string> names, int? projectId, CancellationToken ct = default)
    {
        var vaults = await repo.FindByNamesAsync(names, projectId, ct).ConfigureAwait(false);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var vault in vaults.Where(v => v.ProjectId == null))
            foreach (var secret in vault.Secrets)
                result[secret.Key] = encryption.DecryptValue(secret.EncryptedValue);

        foreach (var vault in vaults.Where(v => v.ProjectId != null))
            foreach (var secret in vault.Secrets)
                result[secret.Key] = encryption.DecryptValue(secret.EncryptedValue);

        return result;
    }

    public async Task<Dictionary<string, string>> ResolveVaultSecretsWithCrossAccessAsync(List<string> names, int projectId, CancellationToken ct = default)
    {
        var vaults = await repo.FindByNamesWithCrossAccessAsync(names, projectId, ct).ConfigureAwait(false);
        return MergeVaultsWithPrecedence(vaults);
    }

    private Dictionary<string, string> MergeVaultsWithPrecedence(List<Data.Entities.Vault> vaults)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var vault in vaults.Where(v => v.ProjectId == null && v.EnvironmentId == null && v.ProjectServerId == null))
            foreach (var secret in vault.Secrets)
                result[secret.Key] = encryption.DecryptValue(secret.EncryptedValue);

        foreach (var vault in vaults.Where(v => v.ProjectId != null))
            foreach (var secret in vault.Secrets)
                result[secret.Key] = encryption.DecryptValue(secret.EncryptedValue);

        foreach (var vault in vaults.Where(v => v.EnvironmentId != null))
            foreach (var secret in vault.Secrets)
                result[secret.Key] = encryption.DecryptValue(secret.EncryptedValue);

        foreach (var vault in vaults.Where(v => v.ProjectServerId != null))
            foreach (var secret in vault.Secrets)
                result[secret.Key] = encryption.DecryptValue(secret.EncryptedValue);

        return result;
    }

    public async Task<(Dictionary<string, string> Vars, HashSet<string> FoundNames)> ResolveVaultSecretsWithNamesAsync(List<string> names, int? projectId, CancellationToken ct = default)
    {
        var vaults = await repo.FindByNamesAsync(names, projectId, ct).ConfigureAwait(false);
        var foundNames = new HashSet<string>(vaults.Select(v => v.Name), StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var vault in vaults.Where(v => v.ProjectId == null))
            foreach (var secret in vault.Secrets)
                result[secret.Key] = encryption.DecryptValue(secret.EncryptedValue);
        foreach (var vault in vaults.Where(v => v.ProjectId != null))
            foreach (var secret in vault.Secrets)
                result[secret.Key] = encryption.DecryptValue(secret.EncryptedValue);

        return (result, foundNames);
    }

    public async Task<(Dictionary<string, string> Vars, HashSet<string> FoundNames)> ResolveVaultSecretsWithCrossAccessAndNamesAsync(List<string> names, int projectId, CancellationToken ct = default)
    {
        var vaults = await repo.FindByNamesWithCrossAccessAsync(names, projectId, ct).ConfigureAwait(false);
        var foundNames = new HashSet<string>(vaults.Select(v => v.Name), StringComparer.OrdinalIgnoreCase);
        return (MergeVaultsWithPrecedence(vaults), foundNames);
    }

    public async Task<List<string>> ExportSecretKeysAsync(int vaultId, CancellationToken ct = default)
    {
        var vault = await repo.GetVaultDetailAsync(vaultId, ct).ConfigureAwait(false);
        if (vault is null) throw new NotFoundException("Vault not found");

        return vault.Secrets.Select(s => s.Key).ToList();
    }

    public async Task<int> ImportSecretsAsync(int vaultId, List<CreateVaultSecretRequest> secrets, CancellationToken ct = default)
    {
        var vault = await repo.FindVaultAsync(vaultId, ct).ConfigureAwait(false);
        if (vault is null) throw new NotFoundException("Vault not found");

        await transaction.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            var createdSecrets = secrets.Select(req => new VaultSecret
            {
                VaultId = vaultId,
                Key = req.Key,
                EncryptedValue = encryption.EncryptValue(req.Value),
                ExpiresAt = req.ExpiresAt
            }).ToList();

            await repo.AddSecretsRangeAsync(createdSecrets, ct).ConfigureAwait(false);

            var versions = createdSecrets.Select(secret => new VaultSecretVersion
            {
                VaultSecretId = secret.Id,
                Key = secret.Key,
                EncryptedValue = secret.EncryptedValue,
                Version = 1,
                ChangeType = ChangeType.Created
            }).ToList();

            await repo.AddSecretVersionsRangeAsync(versions, ct).ConfigureAwait(false);

            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }

        // A bulk import of secret material must leave an audit trail, mirroring CreatedSecret on the
        // single-create path.
        await audit.LogAsync("ImportedSecrets", "Vault", vaultId, $"{secrets.Count} secrets", ct).ConfigureAwait(false);

        return secrets.Count;
    }

    private static string BuildNamesCacheKey(int? projectId, List<int>? accessibleIds)
    {
        var pid = projectId?.ToString() ?? "all";
        if (accessibleIds is null or [])
            return $"{VaultNamesCachePrefix}{pid}";
        return $"{VaultNamesCachePrefix}{pid}:{string.Join(',', accessibleIds.Order())}";
    }

    private void InvalidateNamesCache()
    {
        foreach (var key in _activeNameKeys.Keys)
            cache.Remove(key);
    }

    private static VaultDto MapToDto(Vault v) => new()
    {
        Id = v.Id,
        Name = v.Name,
        Description = v.Description,
        ProjectId = v.ProjectId,
        ProjectName = v.Project?.Name,
        EnvironmentId = v.EnvironmentId,
        EnvironmentName = v.Environment?.Name,
        ProjectServerId = v.ProjectServerId,
        ProjectServerName = v.ProjectServer?.DisplayName,
        SecretCount = v.Secrets.Count,
        CreatedAt = v.CreatedAt,
        UpdatedAt = v.UpdatedAt,
        RowVersion = v.RowVersion
    };
}
