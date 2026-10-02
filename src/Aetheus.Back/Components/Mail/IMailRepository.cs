// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Mail;

public interface IMailRepository
{
    Task<MailState?> GetStateAsync(int serverId, CancellationToken ct = default);
    Task<List<MailDomain>> GetDomainsAsync(int serverId, CancellationToken ct = default);
    Task<(List<MailDomain> Items, int Total)> GetDomainsPagedAsync(
        int serverId, string? search, int page, int pageSize, string? sortBy, bool sortDescending,
        CancellationToken ct = default, IReadOnlyList<GridFilter>? filters = null);
    Task<MailDomain?> GetDomainAsync(int serverId, int domainId, CancellationToken ct = default);
    Task<List<MailAccount>> GetAccountsAsync(int serverId, CancellationToken ct = default);
    Task<(List<MailAccount> Items, int Total)> GetAccountsPagedAsync(
        int serverId, string? search, int page, int pageSize, string? sortBy, bool sortDescending,
        CancellationToken ct = default, IReadOnlyList<GridFilter>? filters = null);
    Task<MailAccount?> GetAccountAsync(int accountId, CancellationToken ct = default);
    Task AddDomainAsync(MailDomain domain, CancellationToken ct = default);
    Task UpdateDomainAsync(MailDomain domain, CancellationToken ct = default);
    Task DeleteDomainAsync(MailDomain domain, CancellationToken ct = default);
    Task AddAccountAsync(MailAccount account, CancellationToken ct = default);
    Task UpdateAccountAsync(MailAccount account, CancellationToken ct = default);
    Task DeleteAccountAsync(MailAccount account, CancellationToken ct = default);
    Task AddTaskAsync(ServerTask task, CancellationToken ct = default);
    Task<List<MailAlias>> GetAliasesAsync(int serverId, CancellationToken ct = default);
    Task<(List<MailAlias> Items, int Total)> GetAliasesPagedAsync(
        int serverId, string? search, int page, int pageSize, string? sortBy, bool sortDescending,
        CancellationToken ct = default, IReadOnlyList<GridFilter>? filters = null);
    Task<MailAlias?> GetAliasAsync(int aliasId, CancellationToken ct = default);
    Task AddAliasAsync(MailAlias alias, CancellationToken ct = default);
    Task UpdateAliasAsync(MailAlias alias, CancellationToken ct = default);
    Task DeleteAliasAsync(MailAlias alias, CancellationToken ct = default);
}
