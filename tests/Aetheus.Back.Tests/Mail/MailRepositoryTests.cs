// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Mail;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests;

public class MailRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly MailRepository _repo;

    public MailRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new MailRepository(_db);
    }

    private MailDomain SeedDomain(int serverId, string name)
    {
        var domain = new MailDomain { ServerId = serverId, Name = name };
        _db.MailDomains.Add(domain);
        _db.SaveChanges();
        return domain;
    }

    [Fact]
    public async Task GetStateAsync_ReturnsStateForServer()
    {
        _db.MailStates.Add(new MailState { ServerId = 1, PostfixVersion = "3.7", IsPostfixRunning = true });
        _db.MailStates.Add(new MailState { ServerId = 2, PostfixVersion = "3.6" });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var state = await _repo.GetStateAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(state);
        Assert.Equal("3.7", state.PostfixVersion);
        Assert.True(state.IsPostfixRunning);
    }

    [Fact]
    public async Task GetStateAsync_NoState_ReturnsNull()
    {
        var state = await _repo.GetStateAsync(99, ct: TestContext.Current.CancellationToken);
        Assert.Null(state);
    }

    [Fact]
    public async Task GetDomainsAsync_ReturnsServerDomainsOrderedByName()
    {
        SeedDomain(1, "zeta.com");
        SeedDomain(1, "alpha.com");
        SeedDomain(2, "other.com");

        var domains = await _repo.GetDomainsAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, domains.Count);
        Assert.Equal("alpha.com", domains[0].Name);
        Assert.Equal("zeta.com", domains[1].Name);
    }

    [Fact]
    public async Task GetDomainsPagedAsync_ReturnsRequestedPageAndTotal()
    {
        SeedDomain(1, "zeta.com");
        SeedDomain(1, "alpha.com");
        SeedDomain(2, "other.com");

        var (items, total) = await _repo.GetDomainsPagedAsync(1, null, 1, 1, "Name", false, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, total);
        Assert.Single(items);
        Assert.Equal("alpha.com", items[0].Name);
    }

    [Fact]
    public async Task GetDomainAsync_MatchingServerAndId_ReturnsDomain()
    {
        var domain = SeedDomain(1, "mail.com");

        var found = await _repo.GetDomainAsync(1, domain.Id, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(found);
        Assert.Equal("mail.com", found.Name);
    }

    [Fact]
    public async Task GetDomainAsync_WrongServer_ReturnsNull()
    {
        var domain = SeedDomain(1, "mail.com");

        var found = await _repo.GetDomainAsync(2, domain.Id, ct: TestContext.Current.CancellationToken);

        Assert.Null(found);
    }

    [Fact]
    public async Task GetAccountsAsync_ReturnsAccountsForServerOrderedByEmail()
    {
        var domain = SeedDomain(1, "mail.com");
        var otherDomain = SeedDomain(2, "other.com");
        _db.MailAccounts.AddRange(
            new MailAccount { MailDomainId = domain.Id, Email = "zoe@mail.com" },
            new MailAccount { MailDomainId = domain.Id, Email = "amy@mail.com" },
            new MailAccount { MailDomainId = otherDomain.Id, Email = "x@other.com" });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var accounts = await _repo.GetAccountsAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, accounts.Count);
        Assert.Equal("amy@mail.com", accounts[0].Email);
        Assert.Equal("zoe@mail.com", accounts[1].Email);
    }

    [Fact]
    public async Task GetAccountsPagedAsync_ReturnsRequestedPageAndTotal()
    {
        var domain = SeedDomain(1, "mail.com");
        _db.MailAccounts.AddRange(
            new MailAccount { MailDomainId = domain.Id, Email = "zoe@mail.com" },
            new MailAccount { MailDomainId = domain.Id, Email = "amy@mail.com" });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetAccountsPagedAsync(1, null, 2, 1, "Email", false, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, total);
        Assert.Single(items);
        Assert.Equal("zoe@mail.com", items[0].Email);
    }

    [Fact]
    public async Task GetAccountAsync_IncludesDomain()
    {
        var domain = SeedDomain(1, "mail.com");
        var account = new MailAccount { MailDomainId = domain.Id, Email = "a@mail.com" };
        _db.MailAccounts.Add(account);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var found = await _repo.GetAccountAsync(account.Id, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(found);
        Assert.NotNull(found.MailDomain);
        Assert.Equal("mail.com", found.MailDomain.Name);
    }

    [Fact]
    public async Task AddDomainAsync_PersistsDomain()
    {
        await _repo.AddDomainAsync(new MailDomain { ServerId = 1, Name = "new.com" }, ct: TestContext.Current.CancellationToken);

        Assert.Single(await _repo.GetDomainsAsync(1, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateDomainAsync_PersistsChanges()
    {
        var domain = SeedDomain(1, "mail.com");
        domain.IsActive = false;
        domain.HasDkim = true;

        await _repo.UpdateDomainAsync(domain, ct: TestContext.Current.CancellationToken);

        var reloaded = await _repo.GetDomainAsync(1, domain.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(reloaded);
        Assert.False(reloaded.IsActive);
        Assert.True(reloaded.HasDkim);
    }

    [Fact]
    public async Task DeleteDomainAsync_RemovesDomain()
    {
        var domain = SeedDomain(1, "mail.com");

        await _repo.DeleteDomainAsync(domain, ct: TestContext.Current.CancellationToken);

        Assert.Empty(await _repo.GetDomainsAsync(1, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddAccountAsync_PersistsAccount()
    {
        var domain = SeedDomain(1, "mail.com");

        await _repo.AddAccountAsync(new MailAccount { MailDomainId = domain.Id, Email = "a@mail.com" }, ct: TestContext.Current.CancellationToken);

        Assert.Single(await _repo.GetAccountsAsync(1, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateAccountAsync_PersistsChanges()
    {
        var domain = SeedDomain(1, "mail.com");
        var account = new MailAccount { MailDomainId = domain.Id, Email = "a@mail.com", QuotaMb = 1024 };
        _db.MailAccounts.Add(account);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        account.QuotaMb = 4096;
        await _repo.UpdateAccountAsync(account, ct: TestContext.Current.CancellationToken);

        var reloaded = await _repo.GetAccountAsync(account.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(reloaded);
        Assert.Equal(4096, reloaded.QuotaMb);
    }

    [Fact]
    public async Task DeleteAccountAsync_RemovesAccount()
    {
        var domain = SeedDomain(1, "mail.com");
        var account = new MailAccount { MailDomainId = domain.Id, Email = "a@mail.com" };
        _db.MailAccounts.Add(account);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.DeleteAccountAsync(account, ct: TestContext.Current.CancellationToken);

        Assert.Empty(await _repo.GetAccountsAsync(1, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddTaskAsync_PersistsTask()
    {
        await _repo.AddTaskAsync(new ServerTask { ServerId = 1, Name = "mail-setup", Command = "echo" }, ct: TestContext.Current.CancellationToken);

        Assert.Equal(1, await _db.Tasks.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAliasesAsync_ReturnsServerAliasesOrderedBySource()
    {
        var domain = SeedDomain(1, "mail.com");
        var otherDomain = SeedDomain(2, "other.com");
        _db.MailAliases.AddRange(
            new MailAlias { MailDomainId = domain.Id, SourceEmail = "zed@mail.com", DestinationEmail = "x@mail.com" },
            new MailAlias { MailDomainId = domain.Id, SourceEmail = "abe@mail.com", DestinationEmail = "y@mail.com" },
            new MailAlias { MailDomainId = otherDomain.Id, SourceEmail = "q@other.com", DestinationEmail = "z@other.com" });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var aliases = await _repo.GetAliasesAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, aliases.Count);
        Assert.Equal("abe@mail.com", aliases[0].SourceEmail);
    }

    [Fact]
    public async Task GetAliasesPagedAsync_ReturnsRequestedPageAndTotal()
    {
        var domain = SeedDomain(1, "mail.com");
        _db.MailAliases.AddRange(
            new MailAlias { MailDomainId = domain.Id, SourceEmail = "zed@mail.com", DestinationEmail = "x@mail.com" },
            new MailAlias { MailDomainId = domain.Id, SourceEmail = "abe@mail.com", DestinationEmail = "y@mail.com" });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetAliasesPagedAsync(
            1, null, 1, 1, "SourceEmail", false, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, total);
        Assert.Single(items);
        Assert.Equal("abe@mail.com", items[0].SourceEmail);
    }

    [Fact]
    public async Task GetAliasAsync_IncludesDomain()
    {
        var domain = SeedDomain(1, "mail.com");
        var alias = new MailAlias { MailDomainId = domain.Id, SourceEmail = "s@mail.com", DestinationEmail = "d@mail.com" };
        _db.MailAliases.Add(alias);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var found = await _repo.GetAliasAsync(alias.Id, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(found);
        Assert.Equal("mail.com", found.MailDomain.Name);
    }

    [Fact]
    public async Task AddAliasAsync_PersistsAlias()
    {
        var domain = SeedDomain(1, "mail.com");

        await _repo.AddAliasAsync(new MailAlias { MailDomainId = domain.Id, SourceEmail = "s@mail.com", DestinationEmail = "d@mail.com" }, ct: TestContext.Current.CancellationToken);

        Assert.Single(await _repo.GetAliasesAsync(1, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateAliasAsync_PersistsChanges()
    {
        var domain = SeedDomain(1, "mail.com");
        var alias = new MailAlias { MailDomainId = domain.Id, SourceEmail = "s@mail.com", DestinationEmail = "old@mail.com" };
        _db.MailAliases.Add(alias);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        alias.DestinationEmail = "new@mail.com";
        await _repo.UpdateAliasAsync(alias, ct: TestContext.Current.CancellationToken);

        var reloaded = await _repo.GetAliasAsync(alias.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(reloaded);
        Assert.Equal("new@mail.com", reloaded.DestinationEmail);
    }

    [Fact]
    public async Task DeleteAliasAsync_RemovesAlias()
    {
        var domain = SeedDomain(1, "mail.com");
        var alias = new MailAlias { MailDomainId = domain.Id, SourceEmail = "s@mail.com", DestinationEmail = "d@mail.com" };
        _db.MailAliases.Add(alias);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.DeleteAliasAsync(alias, ct: TestContext.Current.CancellationToken);

        Assert.Empty(await _repo.GetAliasesAsync(1, ct: TestContext.Current.CancellationToken));
    }

    public void Dispose() => _db.Dispose();
}
