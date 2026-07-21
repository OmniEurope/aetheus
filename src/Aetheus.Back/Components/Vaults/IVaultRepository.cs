// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Vaults;

public interface IVaultRepository
{
    Task<(List<Vault> Items, int TotalCount)> GetVaultsPagedAsync(
        string? search, int? projectId, int? environmentId, int? projectServerId, int page, int pageSize, List<int>? accessibleIds = null, CancellationToken ct = default);

    Task<Vault?> GetVaultDetailAsync(int id, CancellationToken ct = default);

    Task<List<Vault>> FindByNamesAsync(List<string> names, int? projectId, CancellationToken ct = default);

    Task<List<Vault>> FindByNamesWithCrossAccessAsync(List<string> names, int projectId, CancellationToken ct = default);

    Task<List<string>> GetVaultNamesAsync(int? projectId, List<int>? accessibleIds = null, CancellationToken ct = default);

    Task<Vault?> FindVaultAsync(int id, CancellationToken ct = default);

    Task AddVaultAsync(Vault vault, CancellationToken ct = default);

    Task RemoveVaultAsync(Vault vault, CancellationToken ct = default);

    Task<VaultSecret?> FindSecretAsync(int secretId, CancellationToken ct = default);

    Task AddSecretAsync(VaultSecret secret, CancellationToken ct = default);

    Task AddSecretsRangeAsync(List<VaultSecret> secrets, CancellationToken ct = default);

    Task RemoveSecretAsync(VaultSecret secret, CancellationToken ct = default);

    Task AddSecretVersionAsync(VaultSecretVersion version, CancellationToken ct = default);

    Task AddSecretVersionsRangeAsync(List<VaultSecretVersion> versions, CancellationToken ct = default);

    Task<List<VaultSecretVersion>> GetSecretVersionsAsync(int secretId, CancellationToken ct = default);

    Task<int> GetNextVersionAsync(int secretId, CancellationToken ct = default);

    Task<List<VaultSecret>> GetExpiringSecretsAsync(DateTime threshold, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
