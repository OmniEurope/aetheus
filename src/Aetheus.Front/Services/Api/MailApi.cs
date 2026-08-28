// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Aetheus.Shared.DTOs.Organizations;
using Microsoft.AspNetCore.WebUtilities;

namespace Aetheus.Front.Services.Api;

/// <summary>Mail server state, domains, mailboxes, aliases and DKIM key rotation.</summary>
public sealed class MailApi(HttpClient http) : ApiClientBase(http)
{
    public async Task<MailDataDto> GetMailStateAsync(int serverId, CancellationToken ct = default)
    {
        return await Http.GetFromJsonAsync<MailDataDto>($"api/servers/{serverId}/mail", JsonOptions.Web, ct) ?? new();
    }


    public async Task<PaginatedResult<MailDomainDto>> GetMailDomainsPageAsync(
        int serverId, int page, int pageSize, string? search = null, string? sortBy = null, bool sortDescending = false)
    {
        var query = BuildPaginationQuery(page, pageSize, search, sortBy, sortDescending);
        return await Http.GetFromJsonAsync<PaginatedResult<MailDomainDto>>(
            QueryHelpers.AddQueryString($"api/servers/{serverId}/mail/domains", query), JsonOptions.Web) ?? new();
    }


    public async Task<List<MailDomainDto>> GetMailDomainsAsync(int serverId)
    {
        const int pageSize = 100;
        var items = new List<MailDomainDto>();
        for (var page = 1; ; page++)
        {
            var result = await GetMailDomainsPageAsync(serverId, page, pageSize);
            items.AddRange(result.Items);
            if (items.Count >= result.TotalCount || result.Items.Count == 0) return items;
        }
    }


    public async Task<MailDomainDto?> GetMailDomainAsync(int serverId, int domainId, CancellationToken ct = default)
    {
        return await Http.GetFromJsonAsync<MailDomainDto>($"api/servers/{serverId}/mail/domains/{domainId}", JsonOptions.Web, ct);
    }


    public Task<MailDomainDto?> CreateMailDomainAsync(int serverId, CreateMailDomainRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateMailDomainRequest, MailDomainDto>($"api/servers/{serverId}/mail/domains", request, ct);


    public Task<MailDomainDto?> UpdateMailDomainAsync(int serverId, int domainId, UpdateMailDomainRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateMailDomainRequest, MailDomainDto>($"api/servers/{serverId}/mail/domains/{domainId}", request, ct);


    public Task<ApiStatus> DeleteMailDomainAsync(int serverId, int domainId, CancellationToken ct = default)
        => DeleteAsync($"api/servers/{serverId}/mail/domains/{domainId}", ct);


    public async Task<PaginatedResult<MailAccountDto>> GetMailAccountsPageAsync(
        int serverId, int page, int pageSize, string? search = null, string? sortBy = null, bool sortDescending = false)
    {
        var query = BuildPaginationQuery(page, pageSize, search, sortBy, sortDescending);
        return await Http.GetFromJsonAsync<PaginatedResult<MailAccountDto>>(
            QueryHelpers.AddQueryString($"api/servers/{serverId}/mail/accounts", query), JsonOptions.Web) ?? new();
    }


    public async Task<List<MailAccountDto>> GetMailAccountsAsync(int serverId)
    {
        const int pageSize = 100;
        var items = new List<MailAccountDto>();
        for (var page = 1; ; page++)
        {
            var result = await GetMailAccountsPageAsync(serverId, page, pageSize);
            items.AddRange(result.Items);
            if (items.Count >= result.TotalCount || result.Items.Count == 0) return items;
        }
    }


    public Task<MailAccountDto?> CreateMailAccountAsync(int serverId, CreateMailAccountRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateMailAccountRequest, MailAccountDto>($"api/servers/{serverId}/mail/accounts", request, ct);


    public Task<MailAccountDto?> UpdateMailAccountAsync(int serverId, int accountId, UpdateMailAccountRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateMailAccountRequest, MailAccountDto>($"api/servers/{serverId}/mail/accounts/{accountId}", request, ct);


    public Task<ApiStatus> DeleteMailAccountAsync(int serverId, int accountId, CancellationToken ct = default)
        => DeleteAsync($"api/servers/{serverId}/mail/accounts/{accountId}", ct);


    public Task<ApiStatus> ExecuteMailActionAsync(int serverId, MailActionRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<MailActionRequest>($"api/servers/{serverId}/mail/action", request, ct);


    public Task<ApiStatus> GetMailLogsAsync(int serverId, MailLogRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<MailLogRequest>($"api/servers/{serverId}/mail/logs", request, ct);


    public Task<ApiStatus> SetupMailAsync(int serverId, MailSetupRequest request, CancellationToken ct = default)
        => PostJsonNoBodyAsync<MailSetupRequest>($"api/servers/{serverId}/mail/setup", request, ct);


    public async Task<MailDnsRecordsDto?> GetMailDnsRecordsAsync(int serverId, int domainId, CancellationToken ct = default)
    {
        return await Http.GetFromJsonAsync<MailDnsRecordsDto>($"api/servers/{serverId}/mail/domains/{domainId}/dns", JsonOptions.Web, ct);
    }


    public async Task<PaginatedResult<MailAliasDto>> GetMailAliasesPageAsync(
        int serverId, int page, int pageSize, string? search = null, string? sortBy = null, bool sortDescending = false)
    {
        var query = BuildPaginationQuery(page, pageSize, search, sortBy, sortDescending);
        return await Http.GetFromJsonAsync<PaginatedResult<MailAliasDto>>(
            QueryHelpers.AddQueryString($"api/servers/{serverId}/mail/aliases", query), JsonOptions.Web) ?? new();
    }


    public async Task<List<MailAliasDto>> GetMailAliasesAsync(int serverId)
    {
        const int pageSize = 100;
        var items = new List<MailAliasDto>();
        for (var page = 1; ; page++)
        {
            var result = await GetMailAliasesPageAsync(serverId, page, pageSize);
            items.AddRange(result.Items);
            if (items.Count >= result.TotalCount || result.Items.Count == 0) return items;
        }
    }


    public Task<MailAliasDto?> CreateMailAliasAsync(int serverId, int domainId, CreateMailAliasRequest request, CancellationToken ct = default)
        => PostJsonAsync<CreateMailAliasRequest, MailAliasDto>($"api/servers/{serverId}/mail/domains/{domainId}/aliases", request, ct);


    public Task<MailAliasDto?> UpdateMailAliasAsync(int serverId, int aliasId, UpdateMailAliasRequest request, CancellationToken ct = default)
        => PutJsonAsync<UpdateMailAliasRequest, MailAliasDto>($"api/servers/{serverId}/mail/aliases/{aliasId}", request, ct);


    public Task<ApiStatus> DeleteMailAliasAsync(int serverId, int aliasId, CancellationToken ct = default)
        => DeleteAsync($"api/servers/{serverId}/mail/aliases/{aliasId}", ct);


    public Task<DkimRotationResultDto?> RotateMailDkimKeyAsync(int serverId, int domainId, DkimRotationRequest request, CancellationToken ct = default)
        => PostJsonAsync<DkimRotationRequest, DkimRotationResultDto>($"api/servers/{serverId}/mail/domains/{domainId}/dkim/rotate", request, ct);
}
