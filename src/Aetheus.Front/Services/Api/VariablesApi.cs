// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Aetheus.Shared.DTOs.Organizations;
using Microsoft.AspNetCore.WebUtilities;

namespace Aetheus.Front.Services.Api;

/// <summary>Variable libraries and vaults.</summary>
public sealed class VariablesApi(HttpClient http) : ApiClientBase(http)
{
    public async Task<PaginatedResult<VariableLibraryDto>> GetVariableLibrariesAsync(
        int page = 1, int pageSize = 25, string? search = null, int? projectId = null, int? environmentId = null, int? projectServerId = null,
        string? sortBy = null, bool sortDescending = false)
    {
        var query = PageQuery(page, pageSize, search, sortBy, sortDescending);
        if (projectId.HasValue) query["projectId"] = projectId.Value.ToString();
        if (environmentId.HasValue) query["environmentId"] = environmentId.Value.ToString();
        if (projectServerId.HasValue) query["projectServerId"] = projectServerId.Value.ToString();
        return await Http.GetFromJsonAsync<PaginatedResult<VariableLibraryDto>>(
            QueryHelpers.AddQueryString("api/variable-libraries", query), JsonOptions.Web) ?? new();
    }


    public async Task<VariableLibraryDetailDto?> GetVariableLibraryDetailAsync(int id, CancellationToken ct = default)
    {
        return await Http.GetFromJsonAsync<VariableLibraryDetailDto>($"api/variable-libraries/{id}", JsonOptions.Web, ct);
    }


    public async Task<List<VariableLibraryDto>> GetAllVariableLibrariesAsync(int? projectId = null)
    {
        const int pageSize = 100;
        var items = new List<VariableLibraryDto>();
        for (var page = 1; ; page++)
        {
            var result = await GetVariableLibrariesAsync(page, pageSize, projectId: projectId);
            items.AddRange(result.Items);
            if (items.Count >= result.TotalCount || result.Items.Count == 0) return items;
        }
    }


    public async Task<List<string>> GetVariableLibraryNamesAsync(int? projectId = null)
    {
        var query = new Dictionary<string, string?>();
        if (projectId.HasValue) query["projectId"] = projectId.Value.ToString();
        return await Http.GetFromJsonAsync<List<string>>(
            QueryHelpers.AddQueryString("api/variable-libraries/names", query), JsonOptions.Web) ?? [];
    }


    public async Task<PaginatedResult<string>> GetVariableSuggestionKeysAsync(
        int page, int pageSize, int? projectId = null, string? search = null,
        CancellationToken ct = default)
    {
        var query = BuildPaginationQuery(page, pageSize, search, "Key", false);
        if (projectId.HasValue) query["projectId"] = projectId.Value.ToString();
        return await Http.GetFromJsonAsync<PaginatedResult<string>>(
            QueryHelpers.AddQueryString("api/variable-libraries/suggestion-keys", query),
            JsonOptions.Web, ct) ?? new();
    }


    public Task<VariableLibraryDto?> CreateVariableLibraryAsync(CreateVariableLibraryRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateVariableLibraryRequest, VariableLibraryDto>("api/variable-libraries", request, ct);


    public Task<VariableLibraryDto?> UpdateVariableLibraryAsync(int id, UpdateVariableLibraryRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateVariableLibraryRequest, VariableLibraryDto>($"api/variable-libraries/{id}", request, ct);


    public Task<ApiStatus> DeleteVariableLibraryAsync(int id, CancellationToken ct = default)
        => DeleteAsync($"api/variable-libraries/{id}", ct);


    public Task<VariableEntryDto?> CreateVariableEntryAsync(int libraryId, CreateVariableEntryRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateVariableEntryRequest, VariableEntryDto>($"api/variable-libraries/{libraryId}/entries", request, ct);


    public Task<VariableEntryDto?> UpdateVariableEntryAsync(int libraryId, int entryId, UpdateVariableEntryRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateVariableEntryRequest, VariableEntryDto>($"api/variable-libraries/{libraryId}/entries/{entryId}", request, ct);


    public Task<ApiStatus> DeleteVariableEntryAsync(int libraryId, int entryId, CancellationToken ct = default)
        => DeleteAsync($"api/variable-libraries/{libraryId}/entries/{entryId}", ct);


    public async Task<PaginatedResult<VariableEntryDto>> GetVariableEntriesPageAsync(
        int libraryId, int page, int pageSize, string? search = null, string? sortBy = null,
        bool sortDescending = false, CancellationToken ct = default)
    {
        var query = BuildPaginationQuery(page, pageSize, search, sortBy, sortDescending);
        return await Http.GetFromJsonAsync<PaginatedResult<VariableEntryDto>>(
            QueryHelpers.AddQueryString($"api/variable-libraries/{libraryId}/entries", query), JsonOptions.Web, ct) ?? new();
    }


    public async Task<PaginatedResult<VariableEntryVersionDto>> GetVariableEntryVersionsPageAsync(
        int libraryId, int entryId, int page, int pageSize, string? search = null,
        string? sortBy = null, bool sortDescending = false, CancellationToken ct = default)
    {
        var query = BuildPaginationQuery(page, pageSize, search, sortBy, sortDescending);
        return await Http.GetFromJsonAsync<PaginatedResult<VariableEntryVersionDto>>(
            QueryHelpers.AddQueryString(
                $"api/variable-libraries/{libraryId}/entries/{entryId}/versions", query), JsonOptions.Web, ct) ?? new();
    }


    public async Task<List<VariableEntryDto>> ExportVariableEntriesAsync(int libraryId)
    {
        return await Http.GetFromJsonAsync<List<VariableEntryDto>>($"api/variable-libraries/{libraryId}/export", JsonOptions.Web) ?? [];
    }


    public Task<ImportResultDto?> ImportVariableEntriesAsync(int libraryId, List<CreateVariableEntryRequest> entries, CancellationToken ct = default)
        => PostJsonAsync<List<CreateVariableEntryRequest>, ImportResultDto>($"api/variable-libraries/{libraryId}/import", entries, ct);

    public async Task<PaginatedResult<VaultDto>> GetVaultsAsync(
        int page = 1, int pageSize = 25, string? search = null, int? projectId = null, int? environmentId = null, int? projectServerId = null,
        string? sortBy = null, bool sortDescending = false)
    {
        var query = PageQuery(page, pageSize, search, sortBy, sortDescending);
        if (projectId.HasValue) query["projectId"] = projectId.Value.ToString();
        if (environmentId.HasValue) query["environmentId"] = environmentId.Value.ToString();
        if (projectServerId.HasValue) query["projectServerId"] = projectServerId.Value.ToString();
        return await Http.GetFromJsonAsync<PaginatedResult<VaultDto>>(
            QueryHelpers.AddQueryString("api/vaults", query), JsonOptions.Web) ?? new();
    }


    public async Task<VaultDetailDto?> GetVaultDetailAsync(int id, CancellationToken ct = default)
    {
        return await Http.GetFromJsonAsync<VaultDetailDto>($"api/vaults/{id}", JsonOptions.Web, ct);
    }


    public async Task<List<string>> GetVaultNamesAsync(int? projectId = null)
    {
        var query = new Dictionary<string, string?>();
        if (projectId.HasValue) query["projectId"] = projectId.Value.ToString();
        return await Http.GetFromJsonAsync<List<string>>(
            QueryHelpers.AddQueryString("api/vaults/names", query), JsonOptions.Web) ?? [];
    }


    public Task<VaultDto?> CreateVaultAsync(CreateVaultRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateVaultRequest, VaultDto>("api/vaults", request, ct);


    public Task<VaultDto?> UpdateVaultAsync(int id, UpdateVaultRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateVaultRequest, VaultDto>($"api/vaults/{id}", request, ct);


    public Task<ApiStatus> DeleteVaultAsync(int id, CancellationToken ct = default)
        => DeleteAsync($"api/vaults/{id}", ct);


    public Task<VaultSecretDto?> CreateVaultSecretAsync(int vaultId, CreateVaultSecretRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateVaultSecretRequest, VaultSecretDto>($"api/vaults/{vaultId}/secrets", request, ct);


    public Task<VaultSecretDto?> UpdateVaultSecretAsync(int vaultId, int secretId, UpdateVaultSecretRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateVaultSecretRequest, VaultSecretDto>($"api/vaults/{vaultId}/secrets/{secretId}", request, ct);


    public Task<VaultSecretDto?> RotateVaultSecretAsync(int vaultId, int secretId, RotateVaultSecretRequest request, CancellationToken ct = default)
        => PostJsonAsync<RotateVaultSecretRequest, VaultSecretDto>($"api/vaults/{vaultId}/secrets/{secretId}/rotate", request, ct);


    public Task<ApiStatus> DeleteVaultSecretAsync(int vaultId, int secretId, CancellationToken ct = default)
        => DeleteAsync($"api/vaults/{vaultId}/secrets/{secretId}", ct);


    public async Task<List<VaultSecretVersionDto>> GetVaultSecretVersionsAsync(int vaultId, int secretId)
    {
        return await Http.GetFromJsonAsync<List<VaultSecretVersionDto>>($"api/vaults/{vaultId}/secrets/{secretId}/versions", JsonOptions.Web) ?? [];
    }


    public async Task<List<string>> ExportVaultSecretKeysAsync(int vaultId)
    {
        return await Http.GetFromJsonAsync<List<string>>($"api/vaults/{vaultId}/export-keys", JsonOptions.Web) ?? [];
    }


    public Task<ImportResultDto?> ImportVaultSecretsAsync(int vaultId, List<CreateVaultSecretRequest> secrets, CancellationToken ct = default)
        => PostJsonAsync<List<CreateVaultSecretRequest>, ImportResultDto>($"api/vaults/{vaultId}/import", secrets, ct);
}
