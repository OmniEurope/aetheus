// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Servers;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.IntegrationTests;

/// <summary>
/// Exercises <see cref="ServerRepository.RemoveServerAsync"/> against real PostgreSQL. The
/// <c>Vault → ProjectServer → Server</c> chain uses <c>RESTRICT</c> FKs, so a naive
/// <c>Servers.Remove</c> would throw <c>23503</c>; the repository instead deletes leaf-to-root
/// inside a transaction. The InMemory unit provider neither enforces the FKs nor supports
/// <c>ExecuteDelete</c>, so this is the only layer that proves the manual cascade actually works.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ServerRemovalCascadeIntegrationTests(PostgresFixture fixture)
    : RelationalTestBase(fixture)
{
    [Fact]
    public async Task RemoveServerAsync_DeletesProjectServerAndOwnedVaultChain()
    {
        await ResetAndMigrateAsync();
        int serverId;

        await using (var db = NewContext())
        {
            var org = new Organization { Name = "Org rm", Slug = "rmcascade", Description = "d" };
            db.Organizations.Add(org);
            await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

            var project = new Project { Name = "Proj rm", Description = "d", OrganizationId = org.Id };
            var server = new Server { Name = "node-rm", Hostname = "h", OrganizationId = org.Id };
            db.Projects.Add(project);
            db.Servers.Add(server);
            await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
            serverId = server.Id;

            var ps = new ProjectServer
            {
                ProjectId = project.Id,
                ServerId = server.Id,
                Type = ProjectServerType.AgentServer,
                DisplayName = "ps",
                Host = "h"
            };
            db.ProjectServers.Add(ps);
            await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

            var vault = new Vault { Name = "v", Description = "d", ProjectServerId = ps.Id };
            db.Vaults.Add(vault);
            await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

            var secret = new VaultSecret { VaultId = vault.Id, Key = "k", EncryptedValue = "enc" };
            db.VaultSecrets.Add(secret);
            await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

            db.VaultSecretVersions.Add(new VaultSecretVersion
            {
                VaultSecretId = secret.Id,
                Key = "k",
                EncryptedValue = "enc",
                Version = 1
            });
            await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        }

        await using (var db = NewContext())
        {
            var repo = new ServerRepository(db, TimeProvider.System);
            var server = await db.Servers.FirstAsync(s => s.Id == serverId, cancellationToken: TestContext.Current.CancellationToken);
            await repo.RemoveServerAsync(server, ct: TestContext.Current.CancellationToken);
        }

        await using (var verify = NewContext())
        {
            Assert.False(await verify.Servers.AnyAsync(s => s.Id == serverId, cancellationToken: TestContext.Current.CancellationToken));
            Assert.False(await verify.ProjectServers.AnyAsync(ps => ps.ServerId == serverId, cancellationToken: TestContext.Current.CancellationToken));
            // The only seeded vault chain was owned by the deleted ProjectServer.
            Assert.Empty(await verify.Vaults.ToListAsync(cancellationToken: TestContext.Current.CancellationToken));
            Assert.Empty(await verify.VaultSecrets.ToListAsync(cancellationToken: TestContext.Current.CancellationToken));
            Assert.Empty(await verify.VaultSecretVersions.ToListAsync(cancellationToken: TestContext.Current.CancellationToken));
        }
    }
}
